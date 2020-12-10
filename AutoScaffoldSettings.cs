namespace ODataAutoConfiguration
{
  /// <summary>
  /// Bitwise flags to define what scaffolding actions should be performed for an entity set
  /// </summary>
  [System.Flags]
  public enum AutoScaffoldSettings
  {
    /// <summary>
    /// No scaffold settings defined.
    /// </summary>
    None                 = 0 << 0,

    /// <summary>
    /// Enable scaffolding of primary keys.
    /// </summary>
    PrimaryKeys          = 1 << 0,

    /// <summary>
    /// Enable scaffolding of foreign key constraints.
    /// </summary>
    NavigationProperties = 1 << 1,

    /// <summary>
    /// Enable scaffolding of required field constraints.
    /// </summary>
    RequiredFields       = 1 << 2,

    /// <summary>
    /// Enable scaffolding of optional field constraints.
    /// </summary>
    OptionalFields       = 1 << 3,

    /// <summary>
    /// Enable scaffolding of primary and foreign key constraints.
    /// </summary>
    AllKeys              = PrimaryKeys | NavigationProperties,

    /// <summary>
    /// Enable scaffolding of requried and optional field constraints.
    /// </summary>
    AllFields            = RequiredFields | OptionalFields,

    /// <summary>
    /// Enable scaffolding of all available data and schema constraints.
    /// </summary>
    All                  = AllKeys | AllFields
  }
}
