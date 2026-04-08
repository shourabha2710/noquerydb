using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoQueryDB.Api.DatabaseContext;
using Microsoft.EntityFrameworkCore;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/admin/audit-logs")]
    [Authorize(Roles = "CompanyAdmin,SystemAdmin")]
    public class AuditLogsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AuditLogsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetLogs(int page = 1, int pageSize = 50)
        {
            var companyIdClaim = User.FindFirst("companyId")?.Value;

            if (companyIdClaim == null)
                return Unauthorized("Missing companyId in token");

            int companyId = int.Parse(companyIdClaim);

            var logs = await _db.AuditLogs
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.EntityType,
                    x.EntityId,
                    x.Action,
                    x.Description,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}
