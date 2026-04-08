namespace NoQueryDB.Api.Models.Explorer
{
    public class RowInsertRequest
    {
        public string Schema { get; set; } = default!;
        public string Table { get; set; } = default!;
        public Dictionary<string, object?> Values { get; set; } = new();
    }
}
