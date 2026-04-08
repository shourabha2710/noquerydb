namespace NoQueryDB.Api.Models.StoredProcedure
{
    public class ExecuteProcedureRequest
    {
        public string Schema { get; set; } = "";
        public string Procedure { get; set; } = "";
        public List<ProcedureParam> Parameters { get; set; } = new();
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public List<ProcedureFilter> Filters { get; set; } = new();
    }
    public class ProcedureFilter
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "="; // =, !=, >, <, LIKE, IN
        public object? Value { get; set; }
        public object? ValueTo { get; set; } // optional for BETWEEN
    }
    public class ProcedureParam
    {
        public string Name { get; set; } = "";
        public object? Value { get; set; }
    }
}
