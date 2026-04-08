using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Models.Explorer;
using NoQueryDB.Api.Service;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data.SqlClient;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/tables")]
    [Authorize]
    public class TablesExplorerController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _protector;
        private readonly ILogger<TablesExplorerController> _logger;

        public TablesExplorerController(
            AppDbContext db,
            IActiveDatasourceService activeDs,
            ISecretProtector protector,
            ILogger<TablesExplorerController> logger)
        {
            _db = db;
            _activeDs = activeDs;
            _protector = protector;
            _logger = logger;
        }
        // ------------------ COLUMNS ------------------
        [HttpGet("{schema}/{table}/columns")]
        public async Task<IActionResult> GetColumns(string schema, string table)
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
                SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema
  AND TABLE_NAME = @table
ORDER BY ORDINAL_POSITION";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var reader = await cmd.ExecuteReaderAsync();
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
                    scale = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetInt32(5))
                });
            }

            return Ok(list);
        }

        // ------------------ PRIMARY KEYS ------------------
        [HttpGet("{schema}/{table}/primary-keys")]
        public async Task<IActionResult> GetPrimaryKeys(string schema, string table)
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
                SELECT c.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE c
                    ON tc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND tc.TABLE_SCHEMA = @schema
                  AND tc.TABLE_NAME = @table";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var reader = await cmd.ExecuteReaderAsync();
            var list = new List<string>();
            while (await reader.ReadAsync())
                list.Add(reader.GetString(0));

            return Ok(list);
        }

        // ------------------ INDEXES ------------------
        [HttpGet("{schema}/{table}/indexes")]
        public async Task<IActionResult> GetIndexes(string schema, string table)
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
                SELECT i.name AS IndexName, i.type_desc, c.name AS ColumnName
                FROM sys.indexes i
                JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                JOIN sys.tables t ON i.object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @schema AND t.name = @table
                ORDER BY i.name, ic.key_ordinal";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var reader = await cmd.ExecuteReaderAsync();
            var dict = new Dictionary<string, dynamic>();

            while (await reader.ReadAsync())
            {
                var idx = reader.GetString(0);
                if (!dict.ContainsKey(idx))
                {
                    dict[idx] = new
                    {
                        name = idx,
                        type = reader.GetString(1),
                        columns = new List<string>()
                    };
                }
                ((List<string>)dict[idx].columns).Add(reader.GetString(2));
            }

            return Ok(dict.Values);
        }

        // ------------------ CONSTRAINTS ------------------
        [HttpGet("{schema}/{table}/constraints")]
        public async Task<IActionResult> GetTableConstraints(string schema, string table)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var sql = @"
SELECT
    tc.CONSTRAINT_NAME,
    tc.CONSTRAINT_TYPE,
    STRING_AGG(kcu.COLUMN_NAME, ', ') AS Columns,
    cc.definition
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
LEFT JOIN sys.check_constraints cc
    ON cc.name = tc.CONSTRAINT_NAME
WHERE tc.TABLE_SCHEMA = @schema
  AND tc.TABLE_NAME = @table
GROUP BY tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE, cc.definition
ORDER BY tc.CONSTRAINT_TYPE, tc.CONSTRAINT_NAME;
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var result = new List<TableConstraintDto>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new TableConstraintDto
                {
                    Name = reader.GetString(0),
                    Type = reader.GetString(1),
                    Columns = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Definition = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            return Ok(result);
        }

        // ------------------ TRIGGERS ------------------
        [HttpGet("{schema}/{table}/triggers")]
        public async Task<IActionResult> GetTableTriggers(string schema, string table)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var sql = @"
SELECT
    t.name,
    CASE WHEN t.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END AS TriggerType,
    STUFF((
        SELECT ', ' + e.type_desc
        FROM sys.trigger_events e
        WHERE e.object_id = t.object_id
        FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS Events,
    t.is_disabled,
    sm.definition
FROM sys.triggers t
JOIN sys.tables tb ON t.parent_id = tb.object_id
JOIN sys.schemas s ON tb.schema_id = s.schema_id
JOIN sys.sql_modules sm ON t.object_id = sm.object_id
WHERE s.name = @schema
  AND tb.name = @table
ORDER BY t.name;
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var result = new List<TableTriggerDto>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new TableTriggerDto
                {
                    Name = reader.GetString(0),
                    Type = reader.GetString(1),
                    Events = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsEnabled = !reader.GetBoolean(3),
                    Definition = reader.GetString(4)
                });
            }

            return Ok(result);
        }

        // ------------------ TABLE DATA (WITH EXECUTION TIME) ------------------
        [HttpPost("data")]
        public async Task<IActionResult> GetTableData([FromBody] TableDataRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var sw = System.Diagnostics.Stopwatch.StartNew(); // Execution timer

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            // ---------------- Load valid columns ----------------
            var validColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var columnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var colCmd = new SqlCommand(@"
                SELECT COLUMN_NAME, DATA_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table", conn))
            {
                colCmd.Parameters.AddWithValue("@schema", req.Schema);
                colCmd.Parameters.AddWithValue("@table", req.Table);

                using var r = await colCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    validColumns.Add(r.GetString(0));
                    columnTypes[r.GetString(0)] = r.GetString(1);
                }
            }

            if (!validColumns.Any()) return BadRequest("Invalid table or schema");

            // ---------------- Build filters ----------------
            var whereClauses = new List<string>();
            var parameters = new List<SqlParameter>();
            int pIndex = 0;

            foreach (var f in req.Filters ?? Enumerable.Empty<ColumnFilter>())
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
                        var val1 = f.Value?.ToString();
                        var val2 = f.ValueTo?.ToString();
                        if (string.IsNullOrWhiteSpace(val1) || string.IsNullOrWhiteSpace(val2)) continue;
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

            var whereSql = whereClauses.Any() ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            // ---------------- Sorting ----------------
            var orderByColumn = string.IsNullOrWhiteSpace(req.SortColumn) || !validColumns.Contains(req.SortColumn)
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
FROM [{req.Schema}].[{req.Table}]
{whereSql}
{orderBySql}
OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;

SELECT COUNT_BIG(1)
FROM [{req.Schema}].[{req.Table}]
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
            var totalPages = (int)Math.Ceiling(total / (double)req.PageSize);

            sw.Stop();

            return Ok(new
            {
                rows,
                total,
                page = req.Page,
                pageSize = req.PageSize,
                totalPages,
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
        private static object ConvertJsonElement(object value)
        {
            if (value is not JsonElement je)
                return value;

            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString()!,
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l :
                                        je.TryGetDecimal(out var d) ? d :
                                        je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => DBNull.Value,
                _ => je.ToString()!
            };
        }

        // ------------------ DIAGRAM ------------------
        [HttpGet("diagram/{schema}/{table}")]
        public async Task<IActionResult> GetFkDiagram(string schema, string table)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var sql = @"
SELECT
    fk.name AS ForeignKeyName,
    tp.name AS ParentTable,
    cp.name AS ParentColumn,
    tr.name AS ReferencedTable,
    cr.name AS ReferencedColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc
    ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables tp
    ON fkc.parent_object_id = tp.object_id
JOIN sys.columns cp
    ON fkc.parent_object_id = cp.object_id
   AND fkc.parent_column_id = cp.column_id
JOIN sys.tables tr
    ON fkc.referenced_object_id = tr.object_id
JOIN sys.columns cr
    ON fkc.referenced_object_id = cr.object_id
   AND fkc.referenced_column_id = cr.column_id
JOIN sys.schemas s
    ON tp.schema_id = s.schema_id
WHERE s.name = @schema
  AND (tp.name = @table OR tr.name = @table)
ORDER BY fk.name;
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var nodes = new Dictionary<string, TableNodeDto>();
            var edges = new List<ForeignKeyEdgeDto>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var parent = reader["ParentTable"].ToString()!;
                var refTable = reader["ReferencedTable"].ToString()!;

                nodes.TryAdd(parent, new TableNodeDto { Name = parent });
                nodes.TryAdd(refTable, new TableNodeDto { Name = refTable });

                nodes[parent].Columns.Add(reader["ParentColumn"].ToString()!);
                nodes[refTable].Columns.Add(reader["ReferencedColumn"].ToString()!);

                edges.Add(new ForeignKeyEdgeDto
                {
                    Name = reader["ForeignKeyName"].ToString()!,
                    FromTable = parent,
                    FromColumn = reader["ParentColumn"].ToString()!,
                    ToTable = refTable,
                    ToColumn = reader["ReferencedColumn"].ToString()!
                });
            }

            return Ok(new
            {
                tables = nodes.Values,
                relations = edges
            });
        }

        [HttpPut("rows/bulk")]
        public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateRequest req)
        {
            if (req.Keys.Count == 0)
                return BadRequest("No rows selected");

            if (req.Values.Count == 0)
                return BadRequest("No values to update");

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null)
                return BadRequest("No active datasource");

            var ds = await _db.Datasources
                .AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth
                ? null
                : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                // SET clause
                var setSql = string.Join(", ",
                    req.Values.Keys.Select(c => $"[{c}] = @{c}")
                );

                // WHERE (pk OR pk OR pk)
                var whereParts = new List<string>();
                var paramIndex = 0;

                foreach (var row in req.Keys)
                {
                    var cond = new List<string>();
                    foreach (var k in row.Keys)
                    {
                        var p = $"@pk_{paramIndex}_{k}";
                        cond.Add($"[{k}] = {p}");
                        paramIndex++;
                    }
                    whereParts.Add("(" + string.Join(" AND ", cond) + ")");
                }

                var sql = $@"
UPDATE [{req.Schema}].[{req.Table}]
SET {setSql}
WHERE {string.Join(" OR ", whereParts)};
";

                using var cmd = new SqlCommand(sql, conn, tx);

                // SET params
                foreach (var v in req.Values)
                    cmd.Parameters.AddWithValue(
                        "@" + v.Key,
                        ConvertJsonElement(v.Value)
                    );

                // PK params
                paramIndex = 0;
                foreach (var row in req.Keys)
                {
                    foreach (var k in row)
                    {
                        cmd.Parameters.AddWithValue(
                            $"@pk_{paramIndex}_{k.Key}",
                            ConvertJsonElement(k.Value)
                        );
                        paramIndex++;
                    }
                }

                var affected = await cmd.ExecuteNonQueryAsync();
                tx.Commit();

                return Ok(new { affected });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, ex.Message);
            }
        }
        [HttpPut("row")]
        public async Task<IActionResult> UpdateRow([FromBody] RowEditRequest req)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            try
            {
                var dsId = _activeDs.GetActive(userId);
                if (dsId == null)
                    return BadRequest("No active datasource");

                var ds = await _db.Datasources
                    .AsNoTracking()
                    .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

                var password = ds.UseWindowsAuth
                    ? null
                    : _protector.Decrypt(ds.EncryptedPassword!);

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = ds.Server,
                    InitialCatalog = ds.DatabaseName,
                    IntegratedSecurity = ds.UseWindowsAuth,
                    TrustServerCertificate = true
                };

                if (!ds.UseWindowsAuth)
                {
                    builder.UserID = ds.Username;
                    builder.Password = password;
                }

                using var conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();

                // 1️⃣ Detect identity column
                string identityQuery = @"
SELECT TOP 1 name
FROM sys.columns
WHERE object_id = OBJECT_ID(@TableName)
  AND is_identity = 1";

                using var identityCmd = new SqlCommand(identityQuery, conn);
                identityCmd.Parameters.AddWithValue(
                    "@TableName",
                    $"[{req.Schema}].[{req.Table}]"
                );

                var identityCol = await identityCmd.ExecuteScalarAsync() as string;

                if (string.IsNullOrEmpty(identityCol))
                    return BadRequest("Unable to determine primary key column.");

                // 2️⃣ Validate PK from PrimaryKeys ✅
                if (!req.PrimaryKeys.ContainsKey(identityCol))
                    return BadRequest($"Missing primary key column '{identityCol}'.");

                // 3️⃣ Build SET from Values
                var setClauses = req.Values
                    .Select(v => $"[{v.Key}] = @{v.Key}")
                    .ToList();

                if (!setClauses.Any())
                    return BadRequest("No updatable columns found.");

                // 4️⃣ Build WHERE from PrimaryKeys
                var whereClauses = req.PrimaryKeys
                    .Select(k => $"[{k.Key}] = @{k.Key}")
                    .ToList();

                var sql = $@"
UPDATE [{req.Schema}].[{req.Table}]
SET {string.Join(", ", setClauses)}
WHERE {string.Join(" AND ", whereClauses)};";

                using var cmd = new SqlCommand(sql, conn);

                // Parameters for SET
                foreach (var v in req.Values)
                {
                    cmd.Parameters.AddWithValue(
                        $"@{v.Key}",
                        ConvertJsonElement(v.Value)
                    );
                }

                // WHERE parameters
                foreach (var k in req.PrimaryKeys)
                {
                    cmd.Parameters.AddWithValue(
                        $"@{k.Key}",
                        ConvertJsonElement(k.Value)
                    );
                }

                var affected = await cmd.ExecuteNonQueryAsync();

                return Ok(new { affected });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        public sealed class ColumnMeta
        {
            public string Name { get; init; } = default!;
            public string DataType { get; init; } = default!;
            public int? Precision { get; init; }
            public int? Scale { get; init; }
            public int? MaxLength { get; init; }
        }
        private static async Task<Dictionary<string, ColumnMeta>> LoadColumnMetaAsync(
    SqlConnection conn,
    string schema,
    string table)
        {
            var dict = new Dictionary<string, ColumnMeta>(StringComparer.OrdinalIgnoreCase);

            var cmd = new SqlCommand(@"
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema
  AND TABLE_NAME = @table", conn);

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                dict[r.GetString(0)] = new ColumnMeta
                {
                    Name = r.GetString(0),
                    DataType = r.GetString(1),
                    Precision = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetByte(2)),
                    Scale = r.IsDBNull(3) ? null : r.GetInt32(3),
                    MaxLength = r.IsDBNull(4) ? null : r.GetInt32(4)
                };
            }

            return dict;
        }


        [HttpPost("rows/delete")]
        public async Task<IActionResult> DeleteRows([FromBody] RowDeleteRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Table))
                return BadRequest("Table name is required");

            if (req.Keys == null || req.Keys.Count == 0)
                return BadRequest("No rows selected for delete");

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

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                var orGroups = new List<string>();
                var parameters = new List<SqlParameter>();

                for (int i = 0; i < req.Keys.Count; i++)
                {
                    var row = req.Keys[i];
                    if (row.Count == 0) continue;

                    var ands = new List<string>();

                    foreach (var col in row)
                    {
                        var paramName = $"@p{i}_{col.Key}";
                        var value = ConvertJsonElement(col.Value);

                        if (value == null)
                        {
                            ands.Add($"[{col.Key}] IS NULL");
                        }
                        else
                        {
                            ands.Add($"[{col.Key}] = {paramName}");
                            parameters.Add(
                                new SqlParameter(paramName, value)
                            );
                        }
                    }

                    if (ands.Count > 0)
                        orGroups.Add($"({string.Join(" AND ", ands)})");
                }

                if (orGroups.Count == 0)
                    return BadRequest("Invalid delete keys");

                var sql = $@"
DELETE FROM [{req.Schema}].[{req.Table}]
WHERE {string.Join(" OR ", orGroups)};
";

                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.AddRange(parameters.ToArray());

                var deleted = await cmd.ExecuteNonQueryAsync();

                tx.Commit();
                return Ok(new { deleted });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, ex.Message);
            }
        }
        [HttpPost("truncate")]
        [Authorize(Roles = "CompanyAdmin")]
        public async Task<IActionResult> TruncateTable([FromBody] TableDataRequest req)
        {
            static bool IsValidIdentifier(string name) =>
    Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");

            if (!IsValidIdentifier(req.Schema) || !IsValidIdentifier(req.Table))
                return BadRequest("Invalid identifier");
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest();

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                $"TRUNCATE TABLE [{req.Schema}].[{req.Table}]", conn);

            await cmd.ExecuteNonQueryAsync();
            return Ok();
        }

        [HttpPost("drop")]
        [Authorize(Roles = "CompanyAdmin")]
        public async Task<IActionResult> DropTable([FromBody] TableDataRequest req)
        {
            static bool IsValidIdentifier(string name) =>
    Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");

            if (!IsValidIdentifier(req.Schema) || !IsValidIdentifier(req.Table))
                return BadRequest("Invalid identifier");
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest();

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                $"DROP TABLE [{req.Schema}].[{req.Table}]", conn);

            await cmd.ExecuteNonQueryAsync();
            return Ok();
        }
        private static async Task<HashSet<string>> GetNonInsertableColumns(
    SqlConnection conn,
    string schema,
    string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var cmd = new SqlCommand(@"
SELECT c.name
FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @schema
  AND t.name = @table
  AND (c.is_identity = 1 OR c.is_computed = 1)", conn);

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                set.Add(r.GetString(0));

            return set;
        }
        public sealed class ColumnMetaInternal
        {
            public string Name { get; init; } = default!;
            public string DataType { get; init; } = default!;

            public bool IsNullable { get; init; }
            public bool IsIdentity { get; init; }
            public bool IsComputed { get; init; }

            public int? MaxLength { get; init; }
            public int? Precision { get; init; }
            public int? Scale { get; init; }

            public string? DefaultDefinition { get; init; }
        }
        private static SqlDbType MapSqlDbType(string sqlType) => sqlType.ToLower() switch
        {
            "int" => SqlDbType.Int,
            "bigint" => SqlDbType.BigInt,
            "smallint" => SqlDbType.SmallInt,
            "tinyint" => SqlDbType.TinyInt,

            "bit" => SqlDbType.Bit,

            "decimal" or "numeric" => SqlDbType.Decimal,
            "money" => SqlDbType.Money,

            "float" => SqlDbType.Float,
            "real" => SqlDbType.Real,

            "date" => SqlDbType.Date,
            "datetime" => SqlDbType.DateTime,
            "datetime2" => SqlDbType.DateTime2,
            "smalldatetime" => SqlDbType.SmallDateTime,

            "uniqueidentifier" => SqlDbType.UniqueIdentifier,

            "nvarchar" => SqlDbType.NVarChar,
            "varchar" => SqlDbType.VarChar,
            "nchar" => SqlDbType.NChar,
            "char" => SqlDbType.Char,

            _ => SqlDbType.Variant
        };

        private async Task<Dictionary<string, ColumnMetaInternal>> GetColumnMetaInternal(
    SqlConnection conn,
    string schema,
    string table
)
        {
            var sql = @"
SELECT
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.IS_NULLABLE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE,
    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') AS IsComputed,
    dc.definition AS DefaultDefinition
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME)
   AND dc.parent_column_id = COLUMNPROPERTY(
        OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME),
        c.COLUMN_NAME,
        'ColumnId'
   )
WHERE c.TABLE_SCHEMA = @schema
  AND c.TABLE_NAME = @table;
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            using var reader = await cmd.ExecuteReaderAsync();
            var dict = new Dictionary<string, ColumnMetaInternal>(StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync())
            {
                dict[reader["COLUMN_NAME"].ToString()!] = new ColumnMetaInternal
                {
                    Name = reader["COLUMN_NAME"].ToString()!,
                    DataType = reader["DATA_TYPE"].ToString()!,
                    IsNullable = reader["IS_NULLABLE"].ToString() == "YES",
                    MaxLength = reader["CHARACTER_MAXIMUM_LENGTH"] as int?,
                    Precision = reader["NUMERIC_PRECISION"] as byte?,
                    Scale = reader["NUMERIC_SCALE"] as int?,
                    IsIdentity = Convert.ToInt32(reader["IsIdentity"]) == 1,
                    IsComputed = Convert.ToInt32(reader["IsComputed"]) == 1,
                    DefaultDefinition = reader["DefaultDefinition"]?.ToString()
                };
            }

            return dict;
        }


        [HttpPost("row/insert")]
        public async Task<IActionResult> InsertRow([FromBody] RowInsertRequest req)
        {
            if (req.Values == null || req.Values.Count == 0)
                return BadRequest("No values to insert");

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var meta = await GetColumnMetaInternal(conn, req.Schema, req.Table);

            var cols = new List<string>();
            var vals = new List<string>();
            var parameters = new List<SqlParameter>();

            foreach (var kv in req.Values)
            {
                if (!meta.TryGetValue(kv.Key, out var m))
                    continue;

                if (m.IsIdentity || m.IsComputed)
                    continue;

                if (kv.Value == null && m.DefaultDefinition != null)
                    continue; // let SQL default apply

                var p = "@" + kv.Key;
                cols.Add($"[{kv.Key}]");
                vals.Add(p);

                var param = new SqlParameter(p, MapSqlDbType(m.DataType))
                {
                    Value = ConvertJsonElement(kv.Value) ?? DBNull.Value
                };

                if (m.MaxLength.HasValue)
                    param.Size = m.MaxLength.Value;

                var precision = ToByte(m.Precision);
                if (precision.HasValue)
                    param.Precision = precision.Value;

                var scale = ToByte(m.Scale);
                if (scale.HasValue)
                    param.Scale = scale.Value;

                parameters.Add(param);
            }

            if (!cols.Any())
                return BadRequest("No insertable columns");

            var sql = $@"
INSERT INTO [{req.Schema}].[{req.Table}]
({string.Join(", ", cols)})
OUTPUT INSERTED.*
VALUES ({string.Join(", ", vals)});
";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            using var reader = await cmd.ExecuteReaderAsync();
            var inserted = new Dictionary<string, object?>();

            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                    inserted[reader.GetName(i)] =
                        reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            return Ok(inserted);
        }
        private static byte? ToByte(int? value)
        {
            if (!value.HasValue) return null;
            return value.Value > byte.MaxValue ? byte.MaxValue : (byte)value.Value;
        }

        [HttpPost("rows/bulk-insert")]
        public async Task<IActionResult> BulkInsert([FromBody] BulkInsertRequest req)
        {
            if (req.Rows == null || req.Rows.Count == 0)
                return BadRequest("No rows");

            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.AsNoTracking()
                .FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);

            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            var meta = await GetColumnMetaInternal(conn, req.Schema, req.Table);
            using var tx = conn.BeginTransaction();

            var insertedRows = new List<Dictionary<string, object?>>();

            try
            {
                int idx = 0;

                foreach (var row in req.Rows)
                {
                    var cols = new List<string>();
                    var vals = new List<string>();
                    var parameters = new List<SqlParameter>();

                    foreach (var kv in row)
                    {
                        if (!meta.TryGetValue(kv.Key, out var m))
                            continue;

                        if (m.IsIdentity || m.IsComputed)
                            continue;

                        if (kv.Value == null && m.DefaultDefinition != null)
                            continue;

                        var p = $"@p{idx}_{kv.Key}";
                        cols.Add($"[{kv.Key}]");
                        vals.Add(p);

                        var param = new SqlParameter(p, MapSqlDbType(m.DataType))
                        {
                            Value = ConvertJsonElement(kv.Value) ?? DBNull.Value
                        };

                        if (m.MaxLength.HasValue)
                            param.Size = m.MaxLength.Value;

                        var precision = ToByte(m.Precision);
                        if (precision.HasValue)
                            param.Precision = precision.Value;

                        var scale = ToByte(m.Scale);
                        if (scale.HasValue)
                            param.Scale = scale.Value;

                        parameters.Add(param);
                    }

                    if (!cols.Any()) continue;

                    var sql = $@"
INSERT INTO [{req.Schema}].[{req.Table}]
({string.Join(", ", cols)})
OUTPUT INSERTED.*
VALUES ({string.Join(", ", vals)});
";

                    using var cmd = new SqlCommand(sql, conn, tx);
                    cmd.Parameters.AddRange(parameters.ToArray());

                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        var r = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                            r[reader.GetName(i)] =
                                reader.IsDBNull(i) ? null : reader.GetValue(i);

                        insertedRows.Add(r);
                    }

                    idx++;
                }

                tx.Commit();
                return Ok(new { inserted = insertedRows.Count, rows = insertedRows });
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
