namespace NoQueryDB.Api.Models.Explorer
{
    public class RowEditRequest
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public Dictionary<string, object?> Values { get; set; } = new();
        public Dictionary<string, object?> PrimaryKeys { get; set; } = new();
    }
}
