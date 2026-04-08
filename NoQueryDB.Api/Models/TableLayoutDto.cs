namespace NoQueryDB.Api.Models
{
    public class TableLayoutDto
    {
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Table { get; set; }

        public string LayoutMode { get; set; } // db | custom
        public List<string> ColumnOrder { get; set; }

        public Dictionary<string, List<string>> Pinned { get; set; }
        public List<SortRuleDto> Sort { get; set; }
    }

    public class SortRuleDto
    {
        public string Column { get; set; }
        public string Dir { get; set; } // asc | desc
    }
}
