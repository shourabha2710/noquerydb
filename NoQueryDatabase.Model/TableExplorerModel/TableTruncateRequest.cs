namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableTruncateRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string TableName { get; set; }
    }
}
