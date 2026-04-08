namespace NoQueryDB.Api.Models.Explorer
{
    public sealed class ColumnMeta
    {
        public string Name { get; init; } = default!;
        public string DataType { get; init; } = default!;
        public int? Precision { get; init; }
        public int? Scale { get; init; }
        public int? MaxLength { get; init; }
    }
}
