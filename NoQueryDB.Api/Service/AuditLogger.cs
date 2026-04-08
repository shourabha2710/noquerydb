using NoQueryDatabase.Model.Login.NoQueryDatabase.Model.Audit;
using NoQueryDB.Api.DatabaseContext;

namespace NoQueryDB.Api.Service
{
    public class AuditLogger : IAuditLogger
    {
        private readonly AppDbContext _db;

        public AuditLogger(AppDbContext db)
        {
            _db = db;
        }

        public async Task LogAsync(
            HttpContext http,
            int companyId,
            int userId,
            string entityType,
            int? entityId,
            string action,
            string? description = null)
        {
            var log = new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Description = description,
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                UserAgent = http.Request.Headers["User-Agent"].ToString(),
                CreatedAt = DateTime.UtcNow
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
