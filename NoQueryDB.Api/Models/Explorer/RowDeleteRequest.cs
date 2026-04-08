namespace NoQueryDB.Api.Models.Explorer
{
    public class RowDeleteRequest
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public List<Dictionary<string, object?>> Keys { get; set; } = new();
    }
}
