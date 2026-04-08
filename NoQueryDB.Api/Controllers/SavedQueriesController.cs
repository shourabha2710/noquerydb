using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Models.Explorer;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/saved-queries")]
    [Authorize]
    public class SavedQueriesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public SavedQueriesController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            return Ok(_db.SavedQueries.Where(x => x.UserId == userId && x.CompanyId == companyId).OrderByDescending(x => x.CreatedAt).Take(50).AsNoTracking().ToList());
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveQueryRequest req)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            _db.SavedQueries.Add(new SavedQuery
            {
                UserId = userId,
                CompanyId = companyId,
                Name = req.Name,
                SqlText = req.Sql,
                CreatedAt = DateTime.UtcNow 
            });

            await _db.SaveChangesAsync();
            return Ok();
        }
    }

    public class SaveQueryRequest
    {
        public string Name { get; set; } = "";
        public string Sql { get; set; } = "";
    }
}
