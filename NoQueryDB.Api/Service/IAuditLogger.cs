namespace NoQueryDB.Api.Service
{
    public interface IAuditLogger
    {
        Task LogAsync(
            HttpContext http,
            int companyId,
            int userId,
            string entityType,
            int? entityId,
            string action,
            string? description = null
        );
    }
}
