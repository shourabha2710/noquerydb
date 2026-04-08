using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoQueryDatabase.Model.Login;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Service;
using Microsoft.EntityFrameworkCore;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "CompanyAdmin,SystemAdmin")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IAuditLogger _audit;

        public UsersController(AppDbContext db, IAuditLogger audit)
        {
            _db = db;
            _audit = audit;
        }

        // -----------------------------
        // GET ALL USERS
        // -----------------------------
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var companyIdClaim = User.FindFirst("companyId")?.Value;

            if (companyIdClaim == null)
                return Unauthorized("Missing companyId in token");

            int companyId = int.Parse(companyIdClaim);

            var users = await _db.Users
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Email)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.Role,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        // -----------------------------
        // CREATE USER
        // -----------------------------
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest("Email and Role are required");

            var companyIdClaim = User.FindFirst("companyId")?.Value;
            var adminIdClaim = User.FindFirst("userId")?.Value;

            if (companyIdClaim == null || adminIdClaim == null)
                return Unauthorized("Missing companyId or userId in token");

            int companyId = int.Parse(companyIdClaim);
            int adminId = int.Parse(adminIdClaim);

            bool exists = await _db.Users.AnyAsync(x =>
                x.Email == dto.Email &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (exists)
                return BadRequest("User already exists");

            var user = new User
            {
                Email = dto.Email,
                Role = dto.Role,
                CompanyId = companyId,
                IsActive = true
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            try
            {
                await _audit.LogAsync(
                    HttpContext,
                    companyId,
                    adminId,
                    "User",
                    user.Id,
                    "CREATE",
                    $"Created user {user.Email} ({user.Role})"
                );
            }
            catch
            {
                // Ignore audit failure
            }

            return Ok(user);
        }

        // -----------------------------
        // UPDATE USER
        // -----------------------------
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest("Email and Role are required");

            var companyIdClaim = User.FindFirst("companyId")?.Value;
            var adminIdClaim = User.FindFirst("userId")?.Value;

            if (companyIdClaim == null || adminIdClaim == null)
                return Unauthorized("Missing companyId or userId in token");

            int companyId = int.Parse(companyIdClaim);
            int adminId = int.Parse(adminIdClaim);

            var user = await _db.Users.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (user == null)
                return NotFound("User not found");

            if (user.Role == "SystemAdmin")
                return BadRequest("You cannot modify a SystemAdmin");

            bool duplicate = await _db.Users.AnyAsync(x =>
                x.Email == dto.Email &&
                x.Id != id &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (duplicate)
                return BadRequest("Email already exists");

            string oldEmail = user.Email;
            string oldRole = user.Role;

            user.Email = dto.Email;
            user.Role = dto.Role;

            await _db.SaveChangesAsync();

            try
            {
                await _audit.LogAsync(
                    HttpContext,
                    companyId,
                    adminId,
                    "User",
                    id,
                    "UPDATE",
                    $"Updated user: Email {oldEmail} → {user.Email}, Role {oldRole} → {user.Role}"
                );
            }
            catch
            {
                // Ignore audit failure
            }

            return Ok(user);
        }

        // -----------------------------
        // SOFT DELETE USER
        // -----------------------------
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var companyIdClaim = User.FindFirst("companyId")?.Value;
            var adminIdClaim = User.FindFirst("userId")?.Value;

            if (companyIdClaim == null || adminIdClaim == null)
                return Unauthorized("Missing companyId or userId in token");

            int companyId = int.Parse(companyIdClaim);
            int adminId = int.Parse(adminIdClaim);

            var user = await _db.Users.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (user == null)
                return NotFound("User not found");

            if (user.Role == "SystemAdmin")
                return BadRequest("You cannot delete a SystemAdmin");

            user.IsActive = false;
            await _db.SaveChangesAsync();

            try
            {
                await _audit.LogAsync(
                    HttpContext,
                    companyId,
                    adminId,
                    "User",
                    id,
                    "DELETE",
                    $"Soft deleted user {user.Email}"
                );
            }
            catch
            {
                // Ignore audit failure
            }

            return NoContent();
        }
    }

    // -----------------------------
    // DTOs
    // -----------------------------
    public record CreateUserDto(string Email, string Role);
    public record UpdateUserDto(string Email, string Role);

}
