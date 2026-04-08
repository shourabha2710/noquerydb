using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Models.StoredProcedure;
using NoQueryDB.Api.Service;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/procedures")]
    [Authorize]
    public class StoredProcedureExplorerController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _protector;
        private readonly ILogger<StoredProcedureExplorerController> _logger;

        public StoredProcedureExplorerController(
            AppDbContext db,
            IActiveDatasourceService activeDs,
            ISecretProtector protector,
            ILogger<StoredProcedureExplorerController> logger)
        {
            _db = db;
            _activeDs = activeDs;
            _protector = protector;
            _logger = logger;
        }

        // ==============================
        // PROCEDURE DEFINITION
        // ==============================
        [HttpGet("{schema}/{procedure}/definition")]
        public async Task<IActionResult> GetProcedureDefinition(string schema, string procedure)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null)
                return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT OBJECT_DEFINITION(p.object_id)
FROM sys.procedures p
JOIN sys.schemas s ON p.schema_id = s.schema_id
WHERE s.name = @schema AND p.name = @procedure";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@procedure", procedure);

            var def = (string?)await cmd.ExecuteScalarAsync();

            if (string.IsNullOrWhiteSpace(def))
                return Ok("");

            // Convert CREATE -> ALTER like SSMS does
            def = Regex.Replace(
                def,
                @"^\s*CREATE\s+PROCEDURE",
                "ALTER PROCEDURE",
                RegexOptions.IgnoreCase | RegexOptions.Multiline
            );

            return Ok(def);
        }

        // ==============================
        // PROCEDURE PARAMETERS
        // ==============================
        [HttpGet("{schema}/{procedure}/parameters")]
        public async Task<IActionResult> GetProcedureParameters(string schema, string procedure)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();
            var dsId = _activeDs.GetActive(userId);
            if (dsId == null) return BadRequest("No active datasource");

            var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
            var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT
    p.name,
    t.name AS dataType,
    p.max_length,
    p.precision,
    p.scale,
    p.is_output,
    p.is_nullable,
    p.has_default_value
FROM sys.parameters p
JOIN sys.types t ON p.user_type_id = t.user_type_id
JOIN sys.procedures pr ON p.object_id = pr.object_id
JOIN sys.schemas s ON pr.schema_id = s.schema_id
WHERE s.name = @schema
  AND pr.name = @procedure
ORDER BY p.parameter_id";

            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@procedure", procedure);

            var list = new List<object>();

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new
                {
                    name = r.GetString(0),
                    dataType = r.GetString(1),
                    maxLength = r.GetInt16(2),
                    precision = r.GetByte(3),
                    scale = r.GetByte(4),
                    isOutput = r.GetBoolean(5),
                    isNullable = r.GetBoolean(6),
                    hasDefault = r.GetBoolean(7)
                });
            }

            return Ok(list);
        }


        // ==============================
        // EXECUTE PROCEDURE
        // ==============================
        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteProcedure([FromBody] ExecuteProcedureRequest req)
        {
            var sw = Stopwatch.StartNew();

            const int MAX_PAGE_SIZE = 200;
            const int MAX_ROWS_NO_PAGING = 5000;

            try
            {
                var userId = User.GetUserId();
                var companyId = User.GetCompanyId();
                var dsId = _activeDs.GetActive(userId);

                if (dsId == null)
                    return BadRequest("No active datasource");

                var ds = await _db.Datasources.FirstAsync(x => x.Id == dsId && x.CompanyId == companyId);
                var password = ds.UseWindowsAuth ? null : _protector.Decrypt(ds.EncryptedPassword!);

                using var conn = SqlConnectionFactory.Create(ds, password);
                await conn.OpenAsync();

                req.PageNumber = req.PageNumber <= 0 ? 1 : req.PageNumber;
                req.PageSize = Math.Min(req.PageSize <= 0 ? 50 : req.PageSize, MAX_PAGE_SIZE);

                // ------------------------------
                // LOAD PARAMETER METADATA
                // ------------------------------
                var meta = new Dictionary<string, (string type, short len, byte prec, byte scale, bool isOutput)>(StringComparer.OrdinalIgnoreCase);

                using (var metaCmd = conn.CreateCommand())
                {
                    metaCmd.CommandText = @"
SELECT p.name, t.name, p.max_length, p.precision, p.scale, p.is_output
FROM sys.parameters p
JOIN sys.types t ON p.user_type_id = t.user_type_id
JOIN sys.objects o ON p.object_id = o.object_id
JOIN sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @procedure";

                    metaCmd.Parameters.AddWithValue("@schema", req.Schema);
                    metaCmd.Parameters.AddWithValue("@procedure", req.Procedure);

                    using var r = await metaCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        meta[r.GetString(0)] = (
                            r.GetString(1),
                            r.GetInt16(2),
                            r.GetByte(3),
                            r.GetByte(4),
                            r.GetBoolean(5)
                        );
                    }
                }

                bool supportsServerPaging = meta.ContainsKey("@PageNumber") && meta.ContainsKey("@PageSize");
                bool supportsTotalCount = meta.ContainsKey("@TotalCount");

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"[{req.Schema}].[{req.Procedure}]";
                cmd.CommandType = CommandType.StoredProcedure;

                // ------------------------------
                // ADD PARAMETERS
                // ------------------------------
                if (req.Parameters?.Count > 0)
                {
                    foreach (var p in req.Parameters)
                    {
                        if (!meta.TryGetValue(p.Name, out var m))
                            return BadRequest($"Parameter {p.Name} not found");

                        var sqlParam = cmd.CreateParameter();
                        sqlParam.ParameterName = p.Name;
                        sqlParam.Direction = m.isOutput ? ParameterDirection.Output : ParameterDirection.Input;

                        switch (m.type.ToLower())
                        {
                            case "nvarchar":
                                sqlParam.SqlDbType = SqlDbType.NVarChar;
                                sqlParam.Size = m.len == -1 ? -1 : m.len / 2;
                                break;
                            case "varchar":
                                sqlParam.SqlDbType = SqlDbType.VarChar;
                                sqlParam.Size = m.len;
                                break;
                            case "decimal":
                            case "numeric":
                                sqlParam.SqlDbType = SqlDbType.Decimal;
                                sqlParam.Precision = m.prec;
                                sqlParam.Scale = m.scale;
                                break;
                            case "int": sqlParam.SqlDbType = SqlDbType.Int; break;
                            case "bigint": sqlParam.SqlDbType = SqlDbType.BigInt; break;
                            case "bit": sqlParam.SqlDbType = SqlDbType.Bit; break;
                            case "datetime": sqlParam.SqlDbType = SqlDbType.DateTime; break;
                            case "datetime2": sqlParam.SqlDbType = SqlDbType.DateTime2; break;
                            case "uniqueidentifier": sqlParam.SqlDbType = SqlDbType.UniqueIdentifier; break;
                            default:
                                sqlParam.SqlDbType = SqlDbType.NVarChar;
                                sqlParam.Size = -1;
                                break;
                        }

                        sqlParam.Value = m.isOutput ? DBNull.Value : ConvertJsonValue(p.Value);
                        cmd.Parameters.Add(sqlParam);
                    }
                }

                // ------------------------------
                // PAGING PARAMS
                // ------------------------------
                if (supportsServerPaging)
                {
                    cmd.Parameters.Add(new SqlParameter("@PageNumber", req.PageNumber));
                    cmd.Parameters.Add(new SqlParameter("@PageSize", req.PageSize));
                }

                if (supportsTotalCount)
                {
                    cmd.Parameters.Add(new SqlParameter("@TotalCount", SqlDbType.Int) { Direction = ParameterDirection.Output });
                }

                // ------------------------------
                // EXECUTE & READ RESULT
                // ------------------------------
                var resultSets = new List<object>();
                int? fallbackTotalRows = null;

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    do
                    {
                        var rows = new List<Dictionary<string, object>>();
                        var columns = new List<object>();

                        var schema = reader.GetColumnSchema();
                        foreach (var col in schema)
                        {
                            columns.Add(new
                            {
                                name = col.ColumnName,
                                dataType = col.DataTypeName,
                                maxLength = col.ColumnSize,
                                precision = col.NumericPrecision,
                                scale = col.NumericScale,
                                isNullable = col.AllowDBNull
                            });
                        }

                        int rowIndex = 0;
                        int start = (req.PageNumber - 1) * req.PageSize;
                        int end = start + req.PageSize;

                        while (await reader.ReadAsync())
                        {
                            rowIndex++;

                            // ------------------------------
                            // MANUAL PAGINATION FOR NON-PAGED SP
                            // ------------------------------
                            if (!supportsServerPaging)
                            {
                                if (rowIndex > MAX_ROWS_NO_PAGING)
                                {
                                    fallbackTotalRows = null;
                                    break;
                                }

                                if (rowIndex <= start || rowIndex > end)
                                    continue;
                            }

                            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < reader.FieldCount; i++)
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                            // ------------------------------
                            // APPLY FILTERS
                            // ------------------------------
                            if (req.Filters?.Count > 0)
                            {
                                bool include = true;
                                foreach (var f in req.Filters)
                                {
                                    if (!row.TryGetValue(f.Column, out var val)) continue;

                                    include &= f.Operator.ToUpper() switch
                                    {
                                        "=" => object.Equals(val, f.Value),
                                        "!=" => !object.Equals(val, f.Value),
                                        ">" => CompareValues(val, f.Value) > 0,
                                        "<" => CompareValues(val, f.Value) < 0,
                                        ">=" => CompareValues(val, f.Value) >= 0,
                                        "<=" => CompareValues(val, f.Value) <= 0,
                                        "LIKE" => val != null && val.ToString()!.Contains(f.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase),
                                        "IN" => f.Value is IEnumerable<object> list && list.Cast<object>().Any(v => object.Equals(val, v)),
                                        _ => true
                                    };

                                    if (!include) break;
                                }

                                if (!include) continue; // skip row
                            }

                            rows.Add(row);
                        }

                        if (!supportsServerPaging && fallbackTotalRows == null)
                            fallbackTotalRows = rowIndex;

                        resultSets.Add(new { columns, rows });

                    } while (await reader.NextResultAsync());
                }

                // ------------------------------
                // TOTAL ROW COUNT
                // ------------------------------
                int? totalRows = null;

                if (supportsTotalCount && cmd.Parameters["@TotalCount"].Value != DBNull.Value)
                    totalRows = Convert.ToInt32(cmd.Parameters["@TotalCount"].Value);
                else if (!supportsServerPaging)
                    totalRows = fallbackTotalRows;

                // ------------------------------
                // OUTPUT PARAMETERS
                // ------------------------------
                var output = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (SqlParameter p in cmd.Parameters)
                {
                    if (p.Direction != ParameterDirection.Input &&
                        !string.Equals(p.ParameterName, "@TotalCount", StringComparison.OrdinalIgnoreCase))
                    {
                        output[p.ParameterName] = p.Value == DBNull.Value ? null : p.Value;
                    }
                }

                sw.Stop();

                return Ok(new
                {
                    resultSets,
                    paging = new
                    {
                        mode = supportsServerPaging ? "server" : "client",
                        pageNumber = req.PageNumber,
                        pageSize = req.PageSize,
                        totalRows,
                        maxRows = supportsServerPaging ? (int?)null : MAX_ROWS_NO_PAGING
                    },
                    outputParameters = output,
                    executionTimeMs = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Procedure execution failed");
                return StatusCode(500, ex.Message);
            }
        }

        // ------------------------------
        // HELPER: COMPARE VALUES FOR FILTERING
        // ------------------------------
        private static int CompareValues(object? a, object? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            try
            {
                var da = Convert.ToDecimal(a);
                var db = Convert.ToDecimal(b);
                return da.CompareTo(db);
            }
            catch
            {
                return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }






        private static object ConvertJsonValue(object value)
        {
            if (value == null) return DBNull.Value;

            if (value is JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case JsonValueKind.String:
                        return je.GetString();

                    case JsonValueKind.Number:
                        if (je.TryGetInt64(out long l)) return l;
                        if (je.TryGetDecimal(out decimal d)) return d;
                        return je.GetDouble();

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return je.GetBoolean();

                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        return DBNull.Value;

                    default:
                        return je.ToString(); // fallback
                }
            }

            return value;
        }
        [HttpPost("update")]
        public async Task<IActionResult> UpdateProcedure(UpdateProcedureRequest req)
        {
            var sql = req.Sql;

            if (!sql.Trim().StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only ALTER PROCEDURE allowed");
            }

            await _db.Database.ExecuteSqlRawAsync(sql);
            return Ok();
        }

    }
}
