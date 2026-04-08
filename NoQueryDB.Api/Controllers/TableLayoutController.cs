using Dapper;
using Microsoft.AspNetCore.Mvc;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Models;
using System.Data;
using System.Text.Json;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/layout")]
    public class TableLayoutController : ControllerBase
    {
        private readonly IDbConnection _db;

        public TableLayoutController(IDbConnection db)
        {
            _db = db;
        }

        [HttpGet("table")]
        public async Task<IActionResult> GetLayout(
            string database,
            string schema,
            string table)
        {
            var userId = User.GetUserId();

            var layout = await _db.QueryFirstOrDefaultAsync<UserTableLayout>(
                @"SELECT * FROM UserTableLayouts
              WHERE UserId=@userId
                AND DatabaseName=@database
                AND SchemaName=@schema
                AND TableName=@table",
                new { userId, database, schema, table });

            if (layout == null)
                return Ok(null);

            return Ok(new TableLayoutDto
            {
                Database = database,
                Schema = schema,
                Table = table,
                LayoutMode = layout.LayoutMode,
                ColumnOrder = JsonSerializer.Deserialize<List<string>>(layout.ColumnOrderJson),
                Pinned = string.IsNullOrEmpty(layout.PinnedJson)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, List<string>>>(layout.PinnedJson),
                Sort = string.IsNullOrEmpty(layout.SortJson)
                    ? null
                    : JsonSerializer.Deserialize<List<SortRuleDto>>(layout.SortJson)
            });
        }

        [HttpPost("table")]
        public async Task<IActionResult> SaveLayout([FromBody] TableLayoutDto dto)
        {
            var userId = User.GetUserId();

            await _db.ExecuteAsync(
                @"MERGE UserTableLayouts AS t
              USING (SELECT 1 AS X) s
              ON t.UserId=@userId
              AND t.DatabaseName=@Database
              AND t.SchemaName=@Schema
              AND t.TableName=@Table
              WHEN MATCHED THEN
                UPDATE SET
                    ColumnOrderJson=@Order,
                    PinnedJson=@Pinned,
                    SortJson=@Sort,
                    LayoutMode=@LayoutMode,
                    UpdatedAt=SYSDATETIME()
              WHEN NOT MATCHED THEN
                INSERT (UserId,DatabaseName,SchemaName,TableName,
                        ColumnOrderJson,PinnedJson,SortJson,LayoutMode)
                VALUES (@userId,@Database,@Schema,@Table,
                        @Order,@Pinned,@Sort,@LayoutMode);",
                new
                {
                    userId,
                    dto.Database,
                    dto.Schema,
                    dto.Table,
                    Order = JsonSerializer.Serialize(dto.ColumnOrder),
                    Pinned = JsonSerializer.Serialize(dto.Pinned),
                    Sort = JsonSerializer.Serialize(dto.Sort),
                    dto.LayoutMode
                });

            return Ok();
        }

        [HttpDelete("table")]
        public async Task<IActionResult> ResetLayout(
            string database,
            string schema,
            string table)
        {
            var userId = User.GetUserId();

            await _db.ExecuteAsync(
                @"DELETE FROM UserTableLayouts
              WHERE UserId=@userId
                AND DatabaseName=@database
                AND SchemaName=@schema
                AND TableName=@table",
                new { userId, database, schema, table });

            return Ok();
        }
        public static List<string> MergeLayout(
    List<string> dbColumns,
    List<string> userOrder)
        {
            if (userOrder == null || userOrder.Count == 0)
                return dbColumns;

            return userOrder
                .Where(dbColumns.Contains)
                .Concat(dbColumns.Where(c => !userOrder.Contains(c)))
                .ToList();
        }
    }
}
