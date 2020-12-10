using System;
using System.Collections.Generic;
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
    public AutoScaffolder<ContextType> AddSetWithKeys<TEntity>() where TEntity : class, new()
    {
      ConfigureSetWithKeys<TEntity>();
      
      return this;
    }

    /// <summary>
    /// Auto-scaffold a set of TEntity objects with an applicable primary key as defined in the scaffolder's DbContext
    /// and return the EntitySetConfiguration`TEntity instance for additional customization
    /// </summary>
    /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
    public EntitySetConfiguration<TEntity> ConfigureSetWithKeys<TEntity>() where TEntity : class, new()
    {
      // Setup the set and entity configurations
      string setName = _pluralizer.Pluralize(typeof(TEntity).Name);
      EntitySetConfiguration<TEntity> setConfig = _builder.EntitySet<TEntity>(setName);
      EntityTypeConfiguration<TEntity> entityConfig = setConfig.EntityType;

      // Get the CrlType of the configuration
      IEntityType entityType = _context.Model.GetEntityTypes(typeof(TEntity)).SingleOrDefault<IEntityType>();
    
      // Check that the set will have a primary key
      if (entityType.GetKeys() is null || !entityType.GetKeys().Any(key => key.IsPrimaryKey()))
        throw new ODataScaffoldException($"Set '{setName}`{typeof(TEntity).FullName}' will have no configured primary key.");

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

      return setConfig;
    }
  }
}
