using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ODataAutoConfiguration
{
  // Short-hand enum references
  using CC = System.Reflection.CallingConventions;
  using FA = System.Reflection.FieldAttributes;
  using MA = System.Reflection.MethodAttributes;
  using PA = System.Reflection.PropertyAttributes;
  using TA = System.Reflection.TypeAttributes;

  public static class DynamicTypeBuilder
  {
    private static readonly AssemblyName _dynamicAssemblyName;

    static DynamicTypeBuilder()
    {
      // D(ynamic)A(ssembly)_{GUID~format:N}
      _dynamicAssemblyName = new AssemblyName($"DA_{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Create a dynamic Type instance, loaded in a dynamic assembly, which contains a 
    /// set of properties matching the provided IDictionary records. The type also contains
    /// a generic and a full constructor for creating instances with all properties assigned
    /// <remarks>If there are no properties beloning to the object, no full constructor is generated.</remarks>
    /// </summary>
    /// <param name="properties"></param>
    /// <returns></returns>
    public static Type CreateType(IDictionary<string, Type> properties)
    {
      // Create the Type definition builder in the dynamic assembly
      TypeBuilder typeBuilder = GenerateTypeBuilder();

      MethodAttributes constructorAttributes = MA.Public | MA.SpecialName | MA.RTSpecialName;

      ConstructorBuilder genericConstructor = typeBuilder.DefineDefaultConstructor(constructorAttributes);
      // Generate IL for property getter/setter code
      List<FieldBuilder> objFields = new List<FieldBuilder>();
      foreach (KeyValuePair<string, Type> propertyInfo in properties)
        objFields.Add(GenerateProperty(typeBuilder, propertyInfo.Key, propertyInfo.Value));
      
      ConstructorBuilder fullConstructor = typeBuilder.DefineConstructor(constructorAttributes, CC.Standard, properties.Select(kvp => kvp.Value).ToArray());
      
      // Generate the IL for the full constructor after the getter/setter generation
      GenerateFullConstructor(typeBuilder, fullConstructor, objFields, properties);

      // Return the new Type definition
      return typeBuilder.CreateType();
    }

    private static TypeBuilder GenerateTypeBuilder()
    {
      TypeAttributes typeAttributes = TA.Public | TA.Class | TA.AutoClass | TA.AnsiClass | TA.BeforeFieldInit | TA.AutoLayout;
      AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_dynamicAssemblyName, AssemblyBuilderAccess.RunAndCollect);
      ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicAssemblyModule");

      return moduleBuilder.DefineType($"DT_{Guid.NewGuid():N}", typeAttributes, null);
    }

    private static FieldBuilder GenerateProperty(TypeBuilder builder, string propertyName, Type propertyType)
    {
      MethodAttributes getSetAttributes = MA.Public | MA.SpecialName | MA.HideBySig;

      string fieldName = $"_{propertyName}";
      string getterName = $"get{fieldName}";
      string setterName = $"set{fieldName}";

      FieldBuilder fieldBuilder = builder.DefineField(fieldName, propertyType, FA.Private);
      PropertyBuilder propertyBuilder = builder.DefineProperty(propertyName, PA.HasDefault, propertyType, null);
      MethodBuilder getBuilder = builder.DefineMethod(getterName, getSetAttributes, propertyType, Type.EmptyTypes);
      MethodBuilder setBuilder = builder.DefineMethod(setterName, getSetAttributes, propertyType, Type.EmptyTypes);

      // Build IL for get method (get the property value)
      ILGenerator getIL = getBuilder.GetILGenerator();
      getIL.Emit(OpCodes.Ldarg_0);             // Load object instance
      getIL.Emit(OpCodes.Ldfld, fieldBuilder); // Load existing field value
      getIL.Emit(OpCodes.Ret);                 // Return existing field value

      // Build IL for set method (write the provided value to the property)
      ILGenerator setIL = setBuilder.GetILGenerator();
      Label modifyProperty = setIL.DefineLabel();
      Label exitSet = setIL.DefineLabel();

      // Set method set
      setIL.MarkLabel(modifyProperty);         // Mark start of property modification
      setIL.Emit(OpCodes.Ldarg_0);             // Load object instance
      setIL.Emit(OpCodes.Ldarg_1);             // Load new field value parameter
      setIL.Emit(OpCodes.Stfld, fieldBuilder); // Set field value 

      // Set method return
      setIL.Emit(OpCodes.Nop);                 // NOOP
      setIL.MarkLabel(exitSet);                // Mark end of property modification and flow exit
      setIL.Emit(OpCodes.Ret);                 // Return

      // Assign the get/set methods to the property
      propertyBuilder.SetGetMethod(getBuilder);
      propertyBuilder.SetSetMethod(setBuilder);

      return fieldBuilder;
    }

    private static void GenerateFullConstructor(TypeBuilder builder, ConstructorBuilder constructor, IList<FieldBuilder> fields, IDictionary<string, Type> properties)
    {
      Debug.Assert(properties?.Keys is not null);

      if (properties.Keys.Count > (short.MaxValue / 3))
        throw new ArgumentOutOfRangeException(nameof(properties.Keys.Count));

      ILGenerator constructorIL = constructor.GetILGenerator();

      //  Call the base constructor.
      constructorIL.Emit(OpCodes.Ldarg_0);
      var objectCtor = builder.BaseType.GetConstructor(Type.EmptyTypes);
      constructorIL.Emit(OpCodes.Call, objectCtor);

      // Iterate over each property and, using the corresponding FieldBuilder, assign the property values
      short argIndex = 0;
      foreach (KeyValuePair<string, Type> property in properties)
      {
        constructorIL.Emit(OpCodes.Ldarg, (short)0);     // Load generating instance 
        constructorIL.Emit(OpCodes.Ldarg, ++argIndex);   // Load parameter value 
        
        FieldBuilder targetField = fields[argIndex - 1];
        constructorIL.Emit(OpCodes.Stfld, targetField);  // Set field to loaded value
      }
      
      constructorIL.Emit(OpCodes.Nop);                   // NOOP
      constructorIL.Emit(OpCodes.Ret);                   // Return
    }
  }
}
