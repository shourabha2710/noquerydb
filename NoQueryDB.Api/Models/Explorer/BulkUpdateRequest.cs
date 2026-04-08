namespace NoQueryDB.Api.Models.Explorer
{
    public sealed class BulkUpdateRequest
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";

        // selected rows (PK only)
        public List<Dictionary<string, object?>> Keys { get; set; } = new();

        // columns to update
        public Dictionary<string, object?> Values { get; set; } = new();
    }
}
