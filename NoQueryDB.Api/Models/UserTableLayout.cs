namespace NoQueryDB.Api.Models
{
    public class UserTableLayout
    {
        public long Id { get; set; }

        public long UserId { get; set; }
        public long? CompanyId { get; set; }

        public string DatabaseName { get; set; }
        public string SchemaName { get; set; }
        public string TableName { get; set; }

        // JSON payloads
        public string ColumnOrderJson { get; set; }
        public string PinnedJson { get; set; }
        public string SortJson { get; set; }

        public string LayoutMode { get; set; } // "db" | "custom"
        public int Version { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

}
