using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNet.OData.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.OData.Edm;
using Pluralize.NET.Core;

namespace ODataAutoConfiguration
{
    using static EntityConfigurationOptions;

    public class AutoScaffolder<ContextType> where ContextType : DbContext
    {
        // Used to pluralize set names from the entity type name
        private readonly Pluralizer _pluralizer;

        // Database context from which to source keys
        private readonly ContextType _context;

        // ODataConventionModelBuilder against which to construct the entity sets
        private ODataConventionModelBuilder _builder;

        /// <summary>
        /// Instantiate a new AutoScaffolder with the provided DbContext and underlying OData model builder
        /// </summary>
        /// <param name="context"></param>
        /// <param name="builder"></param>
        public AutoScaffolder(ContextType context, ODataModelBuilder builder)
        {
            _context = context;
            _pluralizer = new Pluralizer();
            _builder = builder as ODataConventionModelBuilder;
        }

        /// <summary>
        /// Auto-scaffold a set of TEntity objects based on the specified scaffolding settings
        /// and return the current instance of AutoScaffolder`ContextType for chaining calls
        /// </summary>
        /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
        public AutoScaffolder<ContextType> ScaffoldSet<TEntity>(EntityConfigurationOptions options = None, Action<EntitySetConfiguration<TEntity>> callback = null)
            where TEntity : class, new()
        {
            EntitySetConfiguration<TEntity> entitySet = ConfigureSet<TEntity>();

            if (options.HasFlag(AllowCount))
                entitySet.EntityType.Count();
                
            if (options.HasFlag(AllowSelect))
                entitySet.EntityType.Select();
                
            if (options.HasFlag(AllowExpand))
                entitySet.EntityType.Expand(5);
                
            if (options.HasFlag(AllowFilter))
                entitySet.EntityType.Filter();
                
            if (options.HasFlag(AllowOrderBy))
                entitySet.EntityType.OrderBy();
                
            if (options.HasFlag(AllowPaging))
                entitySet.EntityType.Page();
                
            if (options.HasFlag(DefaultPageSize))
                entitySet.EntityType.Page(null, null);
            
            if (!(callback is null))
                callback.Invoke(entitySet);
            
            return this;
        }

        /// <summary>
        /// Auto-scaffold a set of TEntity objects based on the specified scaffolding settings
        /// and return the current instance of AutoScaffolder`ContextType for chaining calls
        /// </summary>
        /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
        public AutoScaffolder<ContextType> ScaffoldSet<TEntity>(Action<EntityTypeConfiguration<TEntity>> entityConfiguration)
            where TEntity : class, new()
        {
            EntitySetConfiguration<TEntity> entitySet = ConfigureSet<TEntity>();
            if (entityConfiguration != null)
                entityConfiguration(entitySet.EntityType);
            
            return this;
        }

        /// <summary>
        /// Auto-scaffold a set of TEntity objects based on the specified scaffolding settings
        /// and return the EntitySetConfiguration`TEntity instance for additional customization
        /// </summary>
        /// <typeparam name="TEntity">The type of entity which the set comprises</typeparam>
        public EntitySetConfiguration<TEntity> ConfigureSet<TEntity>() 
            where TEntity : class, new()
            => ConfigureSet<TEntity>(out _);
        
        private EntitySetConfiguration<TEntity> ConfigureSet<TEntity>(out string[] primaryKeyProps) 
            where TEntity : class, new()
        {
            // Get the name and context type of the entity set being configured
            string setName = _pluralizer.Pluralize(typeof(TEntity).Name);

            // Forcibly prevent pluralization "Equipment" --> "Equipments"
            if (setName.Contains("quipments", StringComparison.OrdinalIgnoreCase))
                setName = setName.Replace("quipments", "quipment");

            IEntityType entityType = _context.Model.GetEntityTypes(typeof(TEntity)).SingleOrDefault<IEntityType>();

            // Load configuration instances for entity set and type
            EntitySetConfiguration<TEntity> setConfig = _builder.EntitySet<TEntity>(setName);
            EntityTypeConfiguration<TEntity> entityConfig = setConfig.EntityType;
        
            primaryKeyProps = ScaffoldPrimaryKeys(entityConfig, entityType);
            ScaffoldExplicitProperties<TEntity>();

            // Return the generic-typed set configuration instance
            return setConfig;
        }

        private string[] ScaffoldPrimaryKeys<TEntity>(EntityTypeConfiguration<TEntity> entityConfig, IEntityType entityType)
            where TEntity : class, new()
        {
            // Build the properties belonging to the key (name and type)
            Dictionary<string, Type> keyProperties = new Dictionary<string, Type>();
            foreach (IKey entityKey in entityType.GetKeys().Where(key => key.Properties.Any()))
                foreach (IProperty prop in entityKey.Properties.Where(prop => prop.IsPrimaryKey()))
                    if (!keyProperties.TryAdd(prop.Name, prop.ClrType))
                    {
                        Debug.WriteLine($"Replacing key '{prop.Name}' - former type [{keyProperties[prop.Name].FullName}] - new type [{prop.ClrType.FullName}]");

                        keyProperties.Remove(prop.Name);
                        keyProperties.TryAdd(prop.Name, prop.ClrType);
                    }

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

            // Return the name and order of key properties for internal use
            return keyProperties.Keys.ToArray();
        }

        private void ScaffoldExplicitProperties<TEntity>()
            where TEntity : class, new()
        {
            // Filter to find structural types with an explicit exposure attribute
            Func<PropertyInfo, bool> propertyFilter = (prop) => prop.GetCustomAttribute(typeof(ODataExposedAttribute)) != null;

            // Pull the list of properties matching the above filter
            Dictionary<PropertyInfo, EdmMultiplicity> explicitProperties = new Dictionary<PropertyInfo, EdmMultiplicity>();
            foreach (PropertyInfo prop in typeof(TEntity).GetProperties())
                if (propertyFilter(prop))
                    explicitProperties.TryAdd(prop, ((ODataExposedAttribute)prop.GetCustomAttribute(typeof(ODataExposedAttribute))).Multiplicity);

            if (explicitProperties.Keys.Count == 0)
                return;

            // Get (or fail) the structural type associated with the current entity model
            StructuralTypeConfiguration structuralConfig = _builder.StructuralTypes.FirstOrDefault(config => config.ClrType == typeof(TEntity));
            if (structuralConfig is null) 
            {
                Debug.WriteLine($"No structural type exists in builder for TEntity '{typeof(TEntity).FullName}'");
                return;
            }

            // Associate the properties with structural types in the configuration based on the property type.
            // Handles the assignment of Enum, Primitive, Collection, and Complex properties accordingly.
            foreach (KeyValuePair<PropertyInfo, EdmMultiplicity> explicitProperty in explicitProperties)
            {
                Type propertyType = explicitProperty.Key.PropertyType;

                if (propertyType.IsEnum)
                    structuralConfig.AddEnumProperty(explicitProperty.Key);
                else if (propertyType == typeof(string) || propertyType.IsValueType || propertyType.IsPrimitive)
                    structuralConfig.AddProperty(explicitProperty.Key);
                else if (propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType))
                    structuralConfig.AddCollectionProperty(explicitProperty.Key);
                else
                    structuralConfig.AddNavigationProperty(explicitProperty.Key, explicitProperty.Value);
            }
        }
    }
}
