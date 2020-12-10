using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNet.OData.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pluralize.NET.Core;

namespace ODataAutoConfiguration
{
  public class AutoScaffolder<ContextType> where ContextType : DbContext
  {
    private static MethodInfo GetPrimitivePropertyConfigurationMethod;

    static AutoScaffolder()
    {
      // Pull the method information for the static method GetPrimitivePropertyConfiguration
      // to allow reflection to avoid dynamic generic-runtime-parameters and Expression.Convert calls
      GetPrimitivePropertyConfigurationMethod = typeof(AutoScaffolder<>).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                                                                        .FirstOrDefault(method => method.Name == "GetPrimitivePropertyConfiguration");
    }

    // Used to pluralize set names from the entity type name
    private readonly Pluralizer _pluralizer;

    // Database context from which to source keys
    private readonly ContextType _context;

    // ODataModelBuilder against which to construct the entity sets
    private ODataModelBuilder _builder;

    /// <summary>
    /// Instantiate a new AutoScaffolder with the provided DbContext and underlying OData model builder
    /// </summary>
    /// <param name="context"></param>
    /// <param name="builder"></param>
    public AutoScaffolder(ContextType context, ODataModelBuilder builder)
    {
      _context = context;
      _pluralizer = new Pluralizer();
      _builder = builder;
    }

    /// <summary>
    /// Auto-scaffold a set of TEntity objects with an applicable primary key as defined in the scaffolder's DbContext
    /// and return the current instance of AutoScaffolder for chained calls
    /// </summary>
    /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
    public AutoScaffolder<ContextType> AddSetWithKeys<TEntity>(AutoScaffoldSettings settings) 
      where TEntity : class, new()
    {
      // Internal entity set configuration becomes unavailable. Primary keys must be configured by this call.
      if (!settings.HasFlag(AutoScaffoldSettings.PrimaryKeys))
        throw new ODataScaffoldException($"Method 'AddSetWithKeys' must be called with '{nameof(AutoScaffoldSettings.PrimaryKeys)}' flag present. To manually configure primary keys, call method 'ConfigureSetWithKeys'.");

      ConfigureSetWithKeys<TEntity>(settings);
      
      return this;
    }

    /// <summary>
    /// Auto-scaffold a set of TEntity objects with an applicable primary key as defined in the scaffolder's DbContext
    /// and return the EntitySetConfiguration`TEntity instance for additional customization
    /// </summary>
    /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
    public EntitySetConfiguration<TEntity> ConfigureSetWithKeys<TEntity>(AutoScaffoldSettings settings) 
      where TEntity : class, new()
    {
      // Get the name and context type of the entity set being configured
      string setName = _pluralizer.Pluralize(typeof(TEntity).Name);
      IEntityType entityType = _context.Model.GetEntityTypes(typeof(TEntity)).SingleOrDefault<IEntityType>();


      // Warn if the primary key setting was not specified or if the set will have no primary keys configured
      // after execution is complete (these can still be applied manually after execution return so don't error-out)
      Debug.WriteLineIf(!settings.HasFlag(AutoScaffoldSettings.PrimaryKeys), $"Set '{setName}`{typeof(TEntity).FullName}' will be scaffolded without {nameof(AutoScaffoldSettings.PrimaryKeys)} flag present (no primary keys will be configured)");
      Debug.WriteLineIf(settings.HasFlag(AutoScaffoldSettings.PrimaryKeys) && (entityType.GetKeys() is null || !entityType.GetKeys().Any(key => key.IsPrimaryKey())), $"Set '{setName}`{typeof(TEntity).FullName}' will have no configured primary key.");


      // Load configuration instances for entity set and type
      EntitySetConfiguration<TEntity> setConfig = _builder.EntitySet<TEntity>(setName);
      EntityTypeConfiguration<TEntity> entityConfig = setConfig.EntityType;
    

      // Handle configurations per the provided settings
      if (settings.HasFlag(AutoScaffoldSettings.PrimaryKeys))
        ScaffoldPrimaryKeys(entityConfig, entityType);
        
      if (settings.HasFlag(AutoScaffoldSettings.NavigationProperties))
        ScaffoldNavigationProperties(entityConfig, entityType);
        
      if (settings.HasFlag(AutoScaffoldSettings.RequiredFields))
        ScaffoldRequiredFields(entityConfig, entityType);
        
      if (settings.HasFlag(AutoScaffoldSettings.OptionalFields))
        ScaffoldOptionalFields(entityConfig, entityType);


      // Return the generic-typed set configuration instance
      return setConfig;
    }

    private void ScaffoldPrimaryKeys<TEntity>(EntityTypeConfiguration<TEntity> entityConfig, IEntityType entityType)
      where TEntity : class, new()
    {
      // Build the properties belonging to the key (name and type)
      Dictionary<string, Type> keyProperties = new Dictionary<string, Type>();
      foreach (IKey entityKey in entityType.GetKeys().Where(key => key.Properties.Any()))
        foreach (IProperty prop in entityKey.Properties.Where(prop => prop.IsPrimaryKey()))
          keyProperties.Add(prop.Name, prop.ClrType);

      // From the properties, generate a dynamic class instance for the anonymous key
      Type keyType = DynamicTypeBuilder.CreateType(keyProperties);
      
      // Create a parameter for the final key creation and assignment
      ParameterExpression keyEntity = Expression.Parameter(typeof(TEntity), "entity");

      // Get the full constructor from the dynamic type
      Type[] keyEntityArgumentTypes = keyProperties.Select(kvp => kvp.Value).ToArray();
      ConstructorInfo keyEntityConstructor = keyType.GetConstructor(keyEntityArgumentTypes);

      // Populate the expression array representing the constructor's parameter values
      Expression[] keyEntityArguments = keyProperties.Select(kvp => Expression.Property(keyEntity, kvp.Key)).ToArray();

      // Create the NewExpression for the key creation
      NewExpression newKeyExpression = Expression.New(keyEntityConstructor, keyEntityArguments);

      // Convert the newKeyExpression object into a Lambda for the key assignment
      Expression<Func<TEntity, object>> newKeyLambda = Expression.Lambda<Func<TEntity, object>>(newKeyExpression, false, keyEntity);
      
      // Assign the key to the set configuration
      entityConfig.HasKey<object>(newKeyLambda);
    }

    private void ScaffoldNavigationProperties<TEntity>(EntityTypeConfiguration<TEntity> entityConfig, IEntityType entityType)
      where TEntity : class, new()
    {
      throw new NotImplementedException();
    }

    private void ScaffoldRequiredFields<TEntity>(EntityTypeConfiguration<TEntity> entityConfig, IEntityType entityType)
      where TEntity : class, new()
    {
      // Create a parameter for the lambda entity
      ParameterExpression entityParam = Expression.Parameter(typeof(TEntity), "entity");

      // Load the set of non-nullable properties (required fields)
      IEnumerable<IProperty> nullableProps = PropertyRetrieval(entityType, false);

      foreach (IProperty entityProp in nullableProps)
      {
        MemberExpression optionalProperty = Expression.Property(entityParam, entityProp.Name);

        if (entityProp.ClrType.IsValueType)
        {
          // The property is probably not convertible to an object, so reflect against a wrapper to retrieve the property configuration

          // Load the generic method information
          MethodInfo genericGetPrimative = GetPrimitivePropertyConfigurationMethod.MakeGenericMethod(new Type[] { typeof(TEntity), entityProp.ClrType });

          // Execute the generic method to retrieve a PrimitivePropertyConfiguration instance
          object primativeConfigObj = genericGetPrimative.Invoke(null, new object[] { entityConfig, entityParam, optionalProperty });
          PrimitivePropertyConfiguration primativeConfig = (PrimitivePropertyConfiguration)primativeConfigObj;

          // Apply the IsRequired method against the property configuration
          primativeConfig.IsRequired();
        }
        else
        {
          // The property should be convertible to an object, so no reflection is needed
          entityConfig.HasRequired(Expression.Lambda<Func<TEntity, object>>(optionalProperty, false, entityParam));
        }
      }
    }

    private void ScaffoldOptionalFields<TEntity>(EntityTypeConfiguration<TEntity> entityConfig, IEntityType entityType)
      where TEntity : class, new()
    {
      // Create a parameter for the lambda entity
      ParameterExpression entityParam = Expression.Parameter(typeof(TEntity), "entity");

      // Load the set of nullable properties (optional fields)
      IEnumerable<IProperty> optionalProps = PropertyRetrieval(entityType, true);

      foreach (IProperty entityProp in optionalProps)
      {
        MemberExpression optionalProperty = Expression.Property(entityParam, entityProp.Name);

        if (entityProp.ClrType.IsValueType)
        {
          // The property is probably not convertible to an object, so reflect against a wrapper to retrieve the property configuration

          // Load the generic method information
          MethodInfo genericGetPrimative = GetPrimitivePropertyConfigurationMethod.MakeGenericMethod(new Type[] { typeof(TEntity), entityProp.ClrType });

          // Execute the generic method to retrieve a PrimitivePropertyConfiguration instance
          object primativeConfigObj = genericGetPrimative.Invoke(null, new object[] { entityConfig, entityParam, optionalProperty });
          PrimitivePropertyConfiguration primativeConfig = (PrimitivePropertyConfiguration)primativeConfigObj;

          // Apply the IsOptional method against the property configuration
          primativeConfig.IsOptional();
        }
        else
        {
          // The property should be convertible to an object, so no reflection is needed
          entityConfig.HasOptional(Expression.Lambda<Func<TEntity, object>>(optionalProperty, false, entityParam));
        }
      }
    }

    private static IEnumerable<IProperty> PropertyRetrieval(IEntityType entityType, bool nullable)
    {
      IEnumerable<IProperty> properties = entityType.GetProperties();
      return properties.Where(prop => nullable == prop.IsNullable);
    }

    private static PrimitivePropertyConfiguration GetPrimitivePropertyConfiguration<TEntity, TTarget>(
      EntityTypeConfiguration<TEntity> entityConfig, 
      ParameterExpression entityParam, 
      MemberExpression optionalProperty
    ) 
      where TEntity : class, new()
      where TTarget : struct
    {
      return entityConfig.Property(Expression.Lambda<Func<TEntity, TTarget>>(optionalProperty, false, entityParam));
    }
  }
}
