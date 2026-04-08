namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableColumnInfo
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public int? MaxLength { get; set; }
        public string IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string DefaultValue { get; set; }
    }

}
