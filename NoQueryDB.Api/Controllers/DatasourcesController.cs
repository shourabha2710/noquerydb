using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using NoQueryDatabase.Model.Login;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Service;
using static NoQueryDB.Api.Service.EncryptionService;
using NoQueryDB.Api.Models.Datasource;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/admin/datasources")]
    [Authorize(Roles = "CompanyAdmin,SystemAdmin")] // only Admins can manage datasources
    public class DatasourcesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public DatasourcesController(AppDbContext db)
        {
            _db = db;
        }
        [HttpPost("databases")]
        public async Task<IActionResult> LoadDatabases(
    LoadDatabasesDto dto,
    [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            try
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = $"{dto.Server},{dto.Port}",
                    InitialCatalog = "master",
                    IntegratedSecurity = dto.UseWindowsAuth,
                    Encrypt = dto.Encrypted,
                    TrustServerCertificate = true,
                    ConnectTimeout = 5
                };

                if (!dto.UseWindowsAuth)
                {
                    builder.UserID = dto.Username;
                    builder.Password = dto.Password;
                }

                using var conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();

                var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name",
                    conn);

                var list = new List<string>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    list.Add(reader.GetString(0));

                return Ok(list);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        [HttpPost("multi")]
        public async Task<IActionResult> CreateMultipleDatasources(
    CreateMultiDatasourceDto dto,
    [FromServices] ISecretProtector protector,
    [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            foreach (var dbName in dto.Databases)
            {
                var existing = await _db.Datasources.FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId &&
                    x.Server == dto.Server &&
                    x.DatabaseName == dbName);

                // ❌ Active datasource exists → skip
                if (existing != null && existing.DeletedAt == null)
                    continue;

                // ♻️ Restore soft-deleted datasource
                if (existing != null && existing.DeletedAt != null)
                {
                    existing.Name = $"{dto.Name}-{dbName}";
                    existing.Engine = dto.Engine;
                    existing.Port = dto.Port;
                    existing.Username = dto.UseWindowsAuth ? null : dto.Username;
                    existing.EncryptedPassword = dto.UseWindowsAuth
                        ? null
                        : protector.Encrypt(dto.Password!);
                    existing.UseWindowsAuth = dto.UseWindowsAuth;
                    existing.IsEncrypted = dto.Encrypted;
                    existing.DeletedAt = null;
                    existing.DeletedByUserId = null;

                    continue;
                }

                // 🆕 Create new datasource
                _db.Datasources.Add(new Datasource
                {
                    CompanyId = companyId,
                    Name = $"{dto.Name}-{dbName}",
                    Engine = dto.Engine,
                    Server = dto.Server,
                    Port = dto.Port,
                    DatabaseName = dbName,
                    Username = dto.UseWindowsAuth ? null : dto.Username,
                    EncryptedPassword = dto.UseWindowsAuth
                        ? null
                        : protector.Encrypt(dto.Password!),
                    UseWindowsAuth = dto.UseWindowsAuth,
                    IsEncrypted = dto.Encrypted
                });
            }

            await _db.SaveChangesAsync();

            await audit.LogAsync(
                HttpContext,
                companyId,
                userId,
                "Datasource",
                null,
                "BULK_CREATE",
                $"Bulk datasource creation completed for server {dto.Server}");

            return Ok(new { success = true });
        }


        // ✅ GET: List all datasources for the company
        [HttpGet]
        public async Task<IActionResult> GetDatasources()
        {
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            var list = await _db.Datasources
                .Where(x => x.CompanyId == companyId && x.DeletedAt == null)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Engine,
                    x.Server,
                    x.Port,
                    x.DatabaseName,
                    x.Username,
                    x.UseWindowsAuth,
                    x.IsEncrypted
                })
                .ToListAsync();

            return Ok(list);
        }


        // ✅ POST: Test connection (no save)
        [HttpPost("test")]
        public async Task<IActionResult> TestConnection(TestDatasourceDto dto, [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            try
            {
                var connStr = BuildConnectionString(dto, dto.Password);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await audit.LogAsync(HttpContext, companyId, userId, "Datasource", null, "TEST", $"Test connection to {dto.Server}:{dto.Port}");

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                await audit.LogAsync(HttpContext, companyId, userId, "Datasource", null, "TEST_FAIL", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
        }

        // ✅ POST: Create & save datasource CreateDatasource
        [HttpPost]
        public async Task<IActionResult> CreateDatasource(
    [FromBody] CreateDatasourceDto dto,
    [FromServices] ISecretProtector protector,
    [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            // 🔍 Find existing datasource (active OR deleted)
            var existing = await _db.Datasources.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.Name == dto.Name);

            // ❌ Active datasource already exists
            if (existing != null && existing.DeletedAt == null)
                return Conflict("Datasource name already exists.");

            // ♻️ Restore soft-deleted datasource
            if (existing != null && existing.DeletedAt != null)
            {
                existing.Engine = dto.Engine;
                existing.Server = dto.Server;
                existing.Port = dto.Port;
                existing.DatabaseName = dto.DatabaseName;
                existing.Username = dto.UseWindowsAuth ? null : dto.Username;
                existing.EncryptedPassword = dto.UseWindowsAuth
                    ? null
                    : protector.Encrypt(dto.Password!);
                existing.UseWindowsAuth = dto.UseWindowsAuth;
                existing.IsEncrypted = dto.Encrypted;

                existing.DeletedAt = null;
                existing.DeletedByUserId = null;

                await _db.SaveChangesAsync();

                await audit.LogAsync(
                    HttpContext,
                    companyId,
                    userId,
                    "Datasource",
                    existing.Id,
                    "RESTORE",
                    $"Datasource '{existing.Name}' restored");

                return Ok(new
                {
                    existing.Id,
                    existing.Name,
                    restored = true
                });
            }

            // 🆕 Create new datasource
            var entity = new Datasource
            {
                CompanyId = companyId,
                Name = dto.Name,
                Engine = dto.Engine,
                Server = dto.Server,
                Port = dto.Port,
                DatabaseName = dto.DatabaseName,
                Username = dto.UseWindowsAuth ? null : dto.Username,
                EncryptedPassword = dto.UseWindowsAuth
                    ? null
                    : protector.Encrypt(dto.Password!),
                UseWindowsAuth = dto.UseWindowsAuth,
                IsEncrypted = dto.Encrypted
            };

            _db.Datasources.Add(entity);
            await _db.SaveChangesAsync();

            await audit.LogAsync(
                HttpContext,
                companyId,
                userId,
                "Datasource",
                entity.Id,
                "CREATE",
                $"Datasource '{entity.Name}' created");

            return CreatedAtAction(
                nameof(GetDatasources),
                new { id = entity.Id },
                new { entity.Id, entity.Name });
        }


        // ✅ DELETE: Soft delete datasource
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDatasource(int id, [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            var ds = await _db.Datasources
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.DeletedAt == null);

            if (ds == null) return NotFound();

            ds.DeletedAt = DateTime.UtcNow;
            ds.DeletedByUserId = userId;
            await _db.SaveChangesAsync();

            await audit.LogAsync(HttpContext, companyId, userId, "Datasource", ds.Id, "DELETE", $"Datasource '{ds.Name}' deleted");

            return NoContent();
        }

        private string BuildConnectionString(TestDatasourceDto dto, string? password)
        {
            if (dto.Engine != "MSSQL")
                throw new NotSupportedException("Only MSSQL supported currently");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = $"{dto.Server},{dto.Port}",
                IntegratedSecurity = dto.UseWindowsAuth,
                Encrypt = dto.Encrypted,
                TrustServerCertificate = true
            };

            if (!dto.UseWindowsAuth)
            {
                builder.UserID = dto.Username;
                builder.Password = password;
            }

            // 🔑 CRITICAL FIX
            builder.InitialCatalog = string.IsNullOrWhiteSpace(dto.DatabaseName)
                ? "master"      // server-level test
                : dto.DatabaseName;

            return builder.ConnectionString;
        }
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDatasource(
    int id,
    CreateDatasourceDto dto,
    [FromServices] ISecretProtector protector,
    [FromServices] IAuditLogger audit)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);
            var companyId = int.Parse(User.FindFirst("companyId")!.Value);

            var ds = await _db.Datasources.FirstOrDefaultAsync(x =>
                x.Id == id && x.CompanyId == companyId && x.DeletedAt == null);
            if (ds == null) return NotFound();

            ds.Name = dto.Name;
            ds.Engine = dto.Engine;
            ds.Server = dto.Server;
            ds.Port = dto.Port;
            ds.DatabaseName = dto.DatabaseName;
            ds.Username = dto.UseWindowsAuth ? null : dto.Username;
            if (!dto.UseWindowsAuth && !string.IsNullOrEmpty(dto.Password))
                ds.EncryptedPassword = protector.Encrypt(dto.Password);
            ds.UseWindowsAuth = dto.UseWindowsAuth;
            ds.IsEncrypted = dto.Encrypted;

            await _db.SaveChangesAsync();

            await audit.LogAsync(HttpContext, companyId, userId,
                "Datasource", ds.Id, "UPDATE",
                $"Datasource '{ds.Name}' updated");

            return Ok(new { success = true });
        }

    }
}
