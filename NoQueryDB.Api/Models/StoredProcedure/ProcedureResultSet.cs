namespace NoQueryDB.Api.Models.StoredProcedure
{
    public class ProcedureExecutionResponse
    {
        public List<ProcedureResultSet> ResultSets { get; set; } = new();
        public Dictionary<string, object?> OutputParameters { get; set; } = new();
        public long ExecutionTimeMs { get; set; }
    }

    public class ProcedureResultSet
    {
        public string Name { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
        public int RowCount => Rows.Count;
    }
}
