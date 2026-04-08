namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableRowUpdateRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string TableName { get; set; }
        public Dictionary<string, string> RowData { get; set; }
    }
}
