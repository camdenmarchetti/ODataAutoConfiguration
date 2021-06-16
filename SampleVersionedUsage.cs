using System.Linq;
using Microsoft.AspNet.OData.Builder;
using Microsoft.AspNetCore.Mvc;
using ODataAutoConfiguration;

namespace ODataNamespace
{
    using static EntityConfigurationOptions;

    /// <summary>
    /// Represents the model configuration for a controller.
    /// </summary>
    public class ControllerConfiguration : IModelConfiguration
    {
        private readonly EFDatabaseContext _context;
        public ControllerConfiguration(EFDatabaseContext context)
        {
            _context = context;
        }
        
        /// <inheritdoc />
        public void Apply(ODataModelBuilder builder, ApiVersion apiVersion, string routePrefix)
        {
            int preConfigCount = builder.EntitySets.Count();
            AutoScaffolder<EFDatabaseContext> scaffolder = new AutoScaffolder<EFDatabaseContext>(_context, builder);

            scaffolder.ScaffoldSet<ModelOne>(DefaultConfiguration)
                      .ScaffoldSet<ModelTwo>(DefaultConfiguration)
                      .ScaffoldSet<ModelThree>(DefaultConfiguration, (set) => ConfigureModelThree(set));

            System.Diagnostics.Debug.WriteLine($"Controller '{GetType().Name.Replace("ControllerConfiguration", string.Empty)}' configured with [{builder.EntitySets.Count() - preConfigCount}] entity set(s): {{'{string.Join("', '", builder.EntitySets.Skip(preConfigCount).Select(set => set.Name))}'}}");
            
            switch (apiVersion.MajorVersion)
            {
                default:
                    ConfigureV1(builder);
                    break;
            }
        }

        private static void ConfigureModelThree(EntitySetConfiguration<ModelThree> set) 
        {
            set.EntityType.Collection
              .Function(nameof(Controllers.V1.ModelThreeController.ExtraFunctionName))
              .ReturnsFromEntitySet<ModelThree>("ModelThrees");
        }

        private static void ConfigureV1(ODataModelBuilder builder)
        {
            // API V1 function configurations
        }
    }
}
