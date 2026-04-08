namespace NoQueryDatabase.Model.StoredProcedureExplorerModel
{
    public class StoredProcedureExecuteRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string FilterColumn { get; set; }
        public string FilterValue { get; set; }
        public string SortOrder { get; set; }
        public string FilterOperator { get; set; }
        public string Format { get; set; }
        public string SearchKeyword { get; set; }
        public int ExecutionCount { get; set; }
    }
}
