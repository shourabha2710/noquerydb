namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class ObjectRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string FilterColumn { get; set; }
        public string FilterValue { get; set; }
        public string SortOrder { get; set; }
        public string FilterOperator { get; set; }
        
    }

}
