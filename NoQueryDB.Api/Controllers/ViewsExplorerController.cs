using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Models.Explorer;
using NoQueryDB.Api.Models.ViewsExplorer;
using NoQueryDB.Api.Service;
using System.Data.SqlClient;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/views")]
    [Authorize]
    public class ViewsExplorerController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _protector;
        private readonly ILogger<ViewsExplorerController> _logger;

        public ViewsExplorerController(
            AppDbContext db,
            IActiveDatasourceService activeDs,
            ISecretProtector protector,
            ILogger<ViewsExplorerController> logger)
        {
            _db = db;
            _activeDs = activeDs;
            _protector = protector;
            _logger = logger;
        }
        
        // ------------------ VIEW COLUMNS ------------------
        [HttpGet("{schema}/{view}/columns")]
        public async Task<IActionResult> GetViewColumns(string schema, string view)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT
            c.COLUMN_NAME,
            c.DATA_TYPE,
            c.IS_NULLABLE,
            c.CHARACTER_MAXIMUM_LENGTH,
            c.NUMERIC_PRECISION,
            c.NUMERIC_SCALE
        FROM INFORMATION_SCHEMA.COLUMNS c
        INNER JOIN INFORMATION_SCHEMA.VIEWS v
            ON c.TABLE_SCHEMA = v.TABLE_SCHEMA
           AND c.TABLE_NAME = v.TABLE_NAME
        WHERE c.TABLE_SCHEMA = @schema
          AND c.TABLE_NAME = @view
        ORDER BY c.ORDINAL_POSITION";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@view", view);

            using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<object>();

            while (await reader.ReadAsync())
            {
                list.Add(new
                {
                    name = reader.GetString(0),
                    dataType = reader.GetString(1),
                    nullable = reader.GetString(2) == "YES",
                    maxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                    precision = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetByte(4)),
                    scale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                });
            }

            return Ok(list);
        }
        // ------------------ VIEW INDEXES ------------------
        [HttpGet("{schema}/{view}/indexes")]
        public async Task<IActionResult> GetViewIndexes(string schema, string view)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT
            i.name AS IndexName,
            i.type_desc,
            c.name AS ColumnName,
            ic.key_ordinal
        FROM sys.indexes i
        JOIN sys.index_columns ic
            ON i.object_id = ic.object_id
           AND i.index_id = ic.index_id
        JOIN sys.columns c
            ON ic.object_id = c.object_id
           AND ic.column_id = c.column_id
        JOIN sys.views v
            ON i.object_id = v.object_id
        JOIN sys.schemas s
            ON v.schema_id = s.schema_id
        WHERE s.name = @schema
          AND v.name = @view
          AND i.is_hypothetical = 0
          AND i.index_id > 0     -- ignore heap
        ORDER BY i.name, ic.key_ordinal";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@view", view);

            using var reader = await cmd.ExecuteReaderAsync();

            var dict = new Dictionary<string, dynamic>();

            while (await reader.ReadAsync())
            {
                var idxName = reader.GetString(0);

                if (!dict.ContainsKey(idxName))
                {
                    dict[idxName] = new
                    {
                        name = idxName,
                        type = reader.GetString(1),
                        columns = new List<string>()
                    };
                }

                ((List<string>)dict[idxName].columns)
                    .Add(reader.GetString(2));
            }

            return Ok(dict.Values);
        }
        // ------------------ VIEW TRIGGERS ------------------
        [HttpGet("{schema}/{view}/triggers")]
        public async Task<IActionResult> GetViewTriggers(string schema, string view)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT
    t.name,
    'INSTEAD OF' AS TriggerType,
    STUFF((
        SELECT ', ' + e.type_desc
        FROM sys.trigger_events e
        WHERE e.object_id = t.object_id
        FOR XML PATH(''), TYPE
    ).value('.', 'nvarchar(max)'), 1, 2, '') AS Events,
    t.is_disabled,
    sm.definition
FROM sys.triggers t
JOIN sys.views v ON t.parent_id = v.object_id
JOIN sys.schemas s ON v.schema_id = s.schema_id
JOIN sys.sql_modules sm ON t.object_id = sm.object_id
WHERE s.name = @schema
  AND v.name = @view
ORDER BY t.name;
";


            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@view", view);
            using var reader = await cmd.ExecuteReaderAsync();
            var result = new List<TableTriggerDto>();

           
            while (await reader.ReadAsync())
            {
                result.Add(new TableTriggerDto
                {
                    Name = reader.GetString(0),
                    Type = reader.GetString(1), // Always INSTEAD OF
                    Events = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsEnabled = !reader.GetBoolean(3),
                    Definition = reader.GetString(4)
                });
            }

            return Ok(result);
        }

        // ------------------ VIEW DATA (WITH EXECUTION TIME) ------------------
        [HttpPost("data")]
        public async Task<IActionResult> GetViewData([FromBody] ViewsDataRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            // ---------------- Load valid VIEW columns ----------------
            var validColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var columnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var colCmd = new SqlCommand(@"
        SELECT c.COLUMN_NAME, c.DATA_TYPE
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.VIEWS v
          ON c.TABLE_SCHEMA = v.TABLE_SCHEMA
         AND c.TABLE_NAME = v.TABLE_NAME
        WHERE c.TABLE_SCHEMA = @schema
          AND c.TABLE_NAME = @view", conn))
            {
                colCmd.Parameters.AddWithValue("@schema", req.Schema);
                colCmd.Parameters.AddWithValue("@view", req.View); // reuse Table field as View name

                using var r = await colCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    validColumns.Add(r.GetString(0));
                    columnTypes[r.GetString(0)] = r.GetString(1);
                }
            }

            if (!validColumns.Any())
                return BadRequest("Invalid view or schema");

            // ---------------- Build filters ----------------
            var whereClauses = new List<string>();
            var parameters = new List<SqlParameter>();
            int pIndex = 0;

            foreach (var f in req.Filters ?? Enumerable.Empty<ColumnViewsFilter>())
            {
                if (!validColumns.Contains(f.Column))
                    return BadRequest($"Invalid column: {f.Column}");

                var op = f.Operator?.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(op) || !AllowedOperators.Contains(op))
                    return BadRequest($"Operator not allowed: {op}");

                var col = $"[{f.Column}]";
                var sqlType = columnTypes[f.Column];

                object? ConvertValue(object? val)
                {
                    if (val == null) return null;
                    var str = val.ToString();
                    if (string.IsNullOrWhiteSpace(str)) return null;

                    return sqlType switch
                    {
                        "int" or "bigint" => int.Parse(str),
                        "decimal" or "numeric" => decimal.Parse(str),
                        "bit" => bool.Parse(str),
                        "date" or "datetime" or "datetime2" => DateTime.Parse(str),
                        _ => str
                    };
                }

                switch (op)
                {
                    case "IS NULL":
                    case "IS NOT NULL":
                        whereClauses.Add($"{col} {op}");
                        break;

                    case "=":
                    case "!=":
                    case ">":
                    case ">=":
                    case "<":
                    case "<=":
                    case "LIKE":
                    case "NOT LIKE":
                        var valStr = f.Value?.ToString();
                        if (string.IsNullOrWhiteSpace(valStr)) continue;
                        var p = $"@p{pIndex++}";
                        whereClauses.Add($"{col} {op} {p}");
                        parameters.Add(op.Contains("LIKE")
                            ? new SqlParameter(p, $"%{valStr}%")
                            : new SqlParameter(p, ConvertValue(f.Value)!));
                        break;

                    case "BETWEEN":
                        var v1 = f.Value?.ToString();
                        var v2 = f.ValueTo?.ToString();
                        if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2)) continue;
                        var p1 = $"@p{pIndex++}";
                        var p2 = $"@p{pIndex++}";
                        whereClauses.Add($"{col} BETWEEN {p1} AND {p2}");
                        parameters.Add(new SqlParameter(p1, ConvertValue(f.Value)!));
                        parameters.Add(new SqlParameter(p2, ConvertValue(f.ValueTo)!));
                        break;

                    case "IN":
                        var values = f.Value?.ToString()?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(v => v.Trim()).ToList();
                        if (values == null || !values.Any()) continue;
                        var inParams = new List<string>();
                        foreach (var v in values)
                        {
                            var ip = $"@p{pIndex++}";
                            inParams.Add(ip);
                            parameters.Add(new SqlParameter(ip, ConvertValue(v)!));
                        }
                        whereClauses.Add($"{col} IN ({string.Join(",", inParams)})");
                        break;
                }
            }

            var whereSql = whereClauses.Any()
                ? "WHERE " + string.Join(" AND ", whereClauses)
                : "";

            // ---------------- Sorting ----------------
            var orderByColumn = string.IsNullOrWhiteSpace(req.SortColumn)
                || !validColumns.Contains(req.SortColumn)
                ? validColumns.First()
                : req.SortColumn;

            var sortDir = req.SortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
            var orderBySql = $"ORDER BY [{orderByColumn}] {sortDir}";

            // ---------------- Pagination ----------------
            req.Page = req.Page < 1 ? 1 : req.Page;
            req.PageSize = req.PageSize switch
            {
                <= 0 => 10,
                > 100 => 100,
                _ => req.PageSize
            };

            var offset = (req.Page - 1) * req.PageSize;

            var sql = $@"
SELECT *
FROM [{req.Schema}].[{req.View}]
{whereSql}
{orderBySql}
OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;

SELECT COUNT_BIG(1)
FROM [{req.Schema}].[{req.View}]
{whereSql};
";

            parameters.Add(new SqlParameter("@offset", offset));
            parameters.Add(new SqlParameter("@size", req.PageSize));

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            using var reader = await cmd.ExecuteReaderAsync();

            var rows = new List<Dictionary<string, object>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            await reader.NextResultAsync();
            await reader.ReadAsync();
            var total = reader.GetInt64(0);

            sw.Stop();

            return Ok(new
            {
                rows,
                total,
                page = req.Page,
                pageSize = req.PageSize,
                totalPages = (int)Math.Ceiling(total / (double)req.PageSize),
                executionTimeMs = sw.ElapsedMilliseconds
            });
        }
        private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
        {
            "=", "!=", ">", ">=", "<", "<=",
            "LIKE", "NOT LIKE",
            "IN",
            "BETWEEN",
            "IS NULL", "IS NOT NULL"
        };
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
    }

}
