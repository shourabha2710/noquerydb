namespace NoQueryDB.Api.Models.Explorer
{
    public class BulkInsertRequest
    {
        public string Schema { get; set; } = default!;
        public string Table { get; set; } = default!;
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
    }
}
