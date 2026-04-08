using System.ComponentModel.DataAnnotations;

namespace NoQueryDB.Api.Models.Explorer
{
    public class TableDataRequest
    {
        [Required]
        public string Schema { get; set; }

        [Required]
        public string Table { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 25;

        public List<ColumnFilter> Filters { get; set; } = new();
        public string? SortColumn { get; set; }

        [RegularExpression("^(asc|desc)$", ErrorMessage = "SortDirection must be asc or desc")]
        public string SortDirection { get; set; } = "desc";
    }

    public class ColumnFilter
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "="; // =, like, >, <
        public object? Value { get; set; }
        public object? ValueTo { get; set; }
    }
}
