using System;
using Microsoft.OData.Edm;

namespace ODataAutoConfiguration
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    public sealed class ODataExposedAttribute : Attribute
    {
        public ODataExposedAttribute(EdmMultiplicity multiplicity) 
        { 
            this.Multiplicity = multiplicity;
        }

        public EdmMultiplicity Multiplicity { get; internal set; }
    }
}
