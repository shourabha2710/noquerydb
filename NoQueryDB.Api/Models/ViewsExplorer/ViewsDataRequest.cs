using System.ComponentModel.DataAnnotations;

namespace NoQueryDB.Api.Models.ViewsExplorer
{
    public class ViewsDataRequest
    {
        [Required]
        public string Schema { get; set; }

        [Required]
        public string View { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 25;

        public List<ColumnViewsFilter> Filters { get; set; } = new();
        public string? SortColumn { get; set; }

        [RegularExpression("^(asc|desc)$", ErrorMessage = "SortDirection must be asc or desc")]
        public string SortDirection { get; set; } = "desc";
    }

    public class ColumnViewsFilter
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "="; // =, like, >, <
        public object? Value { get; set; }
        public object? ValueTo { get; set; }
    }
}
