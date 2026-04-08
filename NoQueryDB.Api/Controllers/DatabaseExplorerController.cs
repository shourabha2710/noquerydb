using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Service;
using NoQueryDB.Api.Models.Explorer;
using System.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/explorer")]
    [Authorize]
    public class DatabaseExplorerController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _protector;
        private readonly ILogger<DatabaseExplorerController> _logger;

        public DatabaseExplorerController(
            AppDbContext db,
            IActiveDatasourceService activeDs,
            ISecretProtector protector,
            ILogger<DatabaseExplorerController> logger)
        {
            _db = db;
            _activeDs = activeDs;
            _protector = protector;
            _logger = logger;
        }

        // ------------------ TABLES ------------------
        [HttpGet("tables")]
        public async Task<IActionResult> GetTables()
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE='BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            var reader = await cmd.ExecuteReaderAsync();
            var list = new List<object>();
            while (await reader.ReadAsync())
                list.Add(new { schema = reader.GetString(0), name = reader.GetString(1) });

            return Ok(list);
        }

        // ------------------ VIEWS ------------------
        [HttpGet("views")]
        public async Task<IActionResult> GetViews()
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.VIEWS
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            var reader = await cmd.ExecuteReaderAsync();
            var list = new List<object>();
            while (await reader.ReadAsync())
                list.Add(new { schema = reader.GetString(0), name = reader.GetString(1) });

            return Ok(list);
        }

        // ------------------ PROCEDURES ------------------
        [HttpGet("procedures")]
        public async Task<IActionResult> GetProcedures()
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_TYPE = 'PROCEDURE'
                ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME";

            var reader = await cmd.ExecuteReaderAsync();
            var list = new List<object>();
            while (await reader.ReadAsync())
                list.Add(new { schema = reader.GetString(0), name = reader.GetString(1) });

            return Ok(list);
        }

        // ------------------ FUNCTIONS ------------------
        [HttpGet("functions")]
        public async Task<IActionResult> GetFunctions()
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ROUTINE_SCHEMA, ROUTINE_NAME
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_TYPE = 'FUNCTION'
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME";

            var reader = await cmd.ExecuteReaderAsync();
            var list = new List<object>();
            while (await reader.ReadAsync())
                list.Add(new { schema = reader.GetString(0), name = reader.GetString(1) });

            return Ok(list);
        }
        // ------------------ RUN QUERY ------------------
        [HttpPost("query")]
        public async Task<IActionResult> RunQuery([FromBody] RunQueryRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Sql))
                return BadRequest("SQL cannot be empty");

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null)
                return BadRequest("No active datasource");

            var ds = await _db.Datasources
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            await using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = req.Sql;
            cmd.CommandTimeout = 60; // ⏱️ important

            try
            {
                // Try reading result set
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new List<Dictionary<string, object?>>();

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] =
                            reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    result.Add(row);
                }

                return Ok(result);
            }
            catch (SqlException ex)
            {
                // Fallback for non-SELECT (UPDATE/DELETE/DDL)
                if (ex.Number == 0)
                {
                    var rows = await cmd.ExecuteNonQueryAsync();
                    return Ok(new { rowsAffected = rows });
                }

                return BadRequest(new
                {
                    error = ex.Message,
                    line = ex.LineNumber,
                    number = ex.Number
                });
            }
        }

        

        

        

        // ------------------ PREVIEW ------------------
        [HttpGet("tables/{schema}/{table}/preview")]
        public async Task<IActionResult> Preview(string schema, string table)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TOP 100 * FROM [{schema}].[{table}]";

            var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return Ok(rows);
        }

        

        
        


    }
}