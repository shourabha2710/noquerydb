namespace NoQueryDB.Api.Models.Explorer
{
    public class QueryHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DatasourceId { get; set; }
        public string SqlText { get; set; }
        public DateTime ExecutedAt { get; set; }
        public int DurationMs { get; set; }
        public int RowCounts { get; set; }
    }
}
