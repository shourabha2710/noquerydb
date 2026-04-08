using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/query-history")]
    [Authorize]
    public class QueryHistoryController : ControllerBase
    {
        private readonly AppDbContext _db;

        public QueryHistoryController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var userId = User.GetUserId();

            var history = _db.QueryHistories
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.ExecutedAt)
                .Take(50)
                .AsNoTracking()
                .ToList();

            return Ok(history);
        }
    }

}
