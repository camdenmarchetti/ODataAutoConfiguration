namespace ODataAutoConfiguration
{
    [System.Flags]
    public enum EntityConfigurationOptions
    {

        None                 = 0 << 0,

        AllowCount           = 1 << 0,

        AllowSelect          = 1 << 1,

        AllowExpand          = 1 << 2,

        AllowFilter          = 1 << 3,

        AllowOrderBy         = 1 << 4,

        AllowPaging          = 1 << 7,

        DefaultPageSize      = 1 << 8,

        AllowAll             = AllowCount | AllowSelect | AllowExpand | AllowFilter | AllowOrderBy | AllowPaging,

        DefaultConfiguration = AllowAll | DefaultPageSize
    }
}
