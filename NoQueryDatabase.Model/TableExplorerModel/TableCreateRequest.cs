namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableCreateRequest
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string SqlQuery { get; set; }
    }
}
