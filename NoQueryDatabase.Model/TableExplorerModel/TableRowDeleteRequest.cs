namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableRowDeleteRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string TableName { get; set; }
        public Dictionary<string, object> RowData { get; set; } = new();
    }
}
