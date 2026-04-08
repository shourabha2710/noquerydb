namespace NoQueryDatabase.Model.Login
{
    namespace NoQueryDatabase.Model.Audit
    {
        public class AuditLog
        {
            public int Id { get; set; }

            public int CompanyId { get; set; }
            public int UserId { get; set; }

            public string EntityType { get; set; } = null!;
            public int? EntityId { get; set; }

            public string Action { get; set; } = null!;
            public string? Description { get; set; }

            public string? IpAddress { get; set; }
            public string? UserAgent { get; set; }

            public DateTime CreatedAt { get; set; }
        }
    }

}
