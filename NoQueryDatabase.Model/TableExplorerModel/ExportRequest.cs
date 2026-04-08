namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class ExportRequest
    {
        public string DbName { get; set; }
        public string TableName { get; set; }
        public string Keyword { get; set; }
        public string FilterColumn { get; set; }
        public string FilterOperator { get; set; }
        public string FilterValue { get; set; }
        public List<string> VisibleColumns { get; set; }
        public string SortOrder { get; set; }
        public string Format { get; set; }
    }

}
