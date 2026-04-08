using NoQueryDatabase.Utility;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NoQueryDatabase.Data.Contract;
using Microsoft.Extensions.Configuration;

namespace NoQueryDatabase.Data.Implementation
{
    public class ViewsExplorerDataService :IViewsExplorerDataService
    {
        private readonly string _masterConnectionString;
        private readonly IMetadataProvider _metadataProvider;

        public ViewsExplorerDataService(IConfiguration config, IMetadataProvider metadataProvider)
        {
            _masterConnectionString = config.GetConnectionString("MasterDB");
            _metadataProvider = metadataProvider;
        }
        public async Task<(DataTable, int, int, Dictionary<string, string>)> GetViewDataAsync(
    string serverName,
    string dbName,
    string viewName,
    int page,
    int pageSize,
    string filterColumn = null,
    string filterValue = null,
    string sortOrder = "DESC",
    string filterOperator = "LIKE")
        {
            // Step 1: Get connection from store
            var connKey = $"{serverName}_{dbName}";
            var connectedDb = ConnectionStore.Get(connKey);

            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {dbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = dbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };

            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            // Step 2: Parse schema and view name
            var parts = viewName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var view = parts.Length > 1 ? parts[1] : parts[0];

            // Step 3: Prepare filter clause
            var op = (filterOperator ?? "LIKE").ToUpperInvariant();
            bool bindFilterValue = false;
            string whereClause = "";
            string paramValue = null;

            if (!string.IsNullOrWhiteSpace(filterColumn))
            {
                switch (op)
                {
                    case "IS NULL":
                        whereClause = $"WHERE ([{filterColumn}] IS NULL OR [{filterColumn}] = '')";
                        break;
                    case "IS NOT NULL":
                        whereClause = $"WHERE ([{filterColumn}] IS NOT NULL AND [{filterColumn}] <> '')";
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(filterValue))
                        {
                            whereClause = $"WHERE [{filterColumn}] {op} @FilterValue";
                            bindFilterValue = true;
                            paramValue = op.Contains("LIKE", StringComparison.OrdinalIgnoreCase)
                                ? $"%{filterValue}%"
                                : filterValue;
                        }
                        break;
                }
            }

            // Step 4: Get counts
            var totalCountQuery = $"SELECT COUNT(*) FROM [{schema}].[{view}]";
            using var totalCmd = new SqlCommand(totalCountQuery, conn);
            var totalCount = (int)await totalCmd.ExecuteScalarAsync();

            var filteredCountQuery = $"SELECT COUNT(*) FROM [{schema}].[{view}] {whereClause}";
            using var filteredCmd = new SqlCommand(filteredCountQuery, conn);
            if (bindFilterValue)
                filteredCmd.Parameters.AddWithValue("@FilterValue", paramValue);
            var filteredCount = (int)await filteredCmd.ExecuteScalarAsync();

            // Step 5: Get columns for ordering (safe fallback)
            string getFirstColumnQuery = $@"
        SELECT TOP 1 name
        FROM sys.columns
        WHERE object_id = OBJECT_ID('[{schema}].[{view}]')
        ORDER BY column_id";
            using var colCmd = new SqlCommand(getFirstColumnQuery, conn);
            var orderByColumn = (await colCmd.ExecuteScalarAsync())?.ToString() ?? "(SELECT NULL)";

            string orderDirection = sortOrder.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

            // Step 6: Paging query
            string dataQuery = $@"
        SELECT * FROM (
            SELECT *, ROW_NUMBER() OVER (ORDER BY [{orderByColumn}] {orderDirection}) AS RowNum
            FROM [{schema}].[{view}]
            {whereClause}
        ) AS Paged
        WHERE RowNum > @Offset AND RowNum <= (@Offset + @PageSize)";

            using var dataCmd = new SqlCommand(dataQuery, conn);
            dataCmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
            dataCmd.Parameters.AddWithValue("@PageSize", pageSize);
            if (bindFilterValue)
                dataCmd.Parameters.AddWithValue("@FilterValue", paramValue);

            var dt = new DataTable();
            using (var reader = await dataCmd.ExecuteReaderAsync())
            {
                dt.Load(reader);
            }

            // Step 7: Column metadata
            var columnTypes = await _metadataProvider.GetMetadataAsync(serverName, dbName, $"ViewColumnTypes_{viewName}", async () =>
            {
                var types = new Dictionary<string, string>();
                var typeQuery = @"
        SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @View";
                using var typeCmd = new SqlCommand(typeQuery, conn);
                typeCmd.Parameters.AddWithValue("@Schema", schema);
                typeCmd.Parameters.AddWithValue("@View", view);

                using (var typeReader = await typeCmd.ExecuteReaderAsync())
                {
                    while (await typeReader.ReadAsync())
                    {
                        var column = typeReader.GetString(0);
                        var dataType = typeReader.GetString(1);
                        var length = typeReader.IsDBNull(2) ? null : typeReader.GetValue(2)?.ToString();

                        var fullType = (dataType is "varchar" or "nvarchar" or "char")
                            ? $"{dataType}({(length == "-1" ? "MAX" : length)})"
                            : dataType;

                        types[column] = fullType;
                    }
                }
                return types;
            });

            return (dt, filteredCount, totalCount, columnTypes);
        }

        public async Task<(DataTable, int, int)> SearchViewsDataAsync(
            string serverName,
     string dbName,
     string tableName,
     string keyword,
     int page = 1,
     int pageSize = 10)
        {
            // Step 1: Try to get dynamic connection
            var connKey = $"{serverName}_{dbName}";
            var connectedDb = ConnectionStore.Get(connKey);

            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {dbName}");

            // Step 2: Build connection string
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = dbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };

            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            var connectionString = builder.ConnectionString;

            var dt = new DataTable();
            int filteredCount = 0;
            int totalCount = 0;
            int offset = (page - 1) * pageSize;
            string likeParam = $"%{keyword}%";

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            var parts = tableName.Split('.');
            var schema = parts[0];
            var table = parts[1];
            // Step 1: Get searchable column names
            var colQuery = @"
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = @Table
  AND TABLE_SCHEMA = @Schema";

            using var colCmd = new SqlCommand(colQuery, conn);
            colCmd.Parameters.AddWithValue("@Table", table);
            colCmd.Parameters.AddWithValue("@Schema", schema);

            var columns = new List<string>();
            using (var reader = await colCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(0));
                }
            }

            if (!columns.Any())
                return (dt, 0, 0);

            // Step 2: Build WHERE clause
            var whereClause = string.Join(" OR ", columns.Select(c => $"CAST([{c}] AS NVARCHAR(MAX)) LIKE @Keyword"));

            // Step 3A: Paged Data Query
            var dataQuery = $@"
;WITH FilteredData AS (
    SELECT *, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS RowNum
    FROM [{dbName}].[{schema}].[{table}]
    WHERE {whereClause}
)
SELECT * FROM FilteredData
WHERE RowNum > @Offset AND RowNum <= (@Offset + @PageSize);";

            using (var dataCmd = new SqlCommand(dataQuery, conn))
            {
                dataCmd.Parameters.AddWithValue("@Keyword", likeParam);
                dataCmd.Parameters.AddWithValue("@Offset", offset);
                dataCmd.Parameters.AddWithValue("@PageSize", pageSize);

                using var reader = await dataCmd.ExecuteReaderAsync();
                dt.Load(reader);
            }

            // Step 3B: Filtered count query
            var countQuery = $@"
SELECT COUNT(*) 
FROM [{dbName}].[{schema}].[{table}]
WHERE {whereClause};";

            using (var countCmd = new SqlCommand(countQuery, conn))
            {
                countCmd.Parameters.AddWithValue("@Keyword", likeParam);
                filteredCount = (int)await countCmd.ExecuteScalarAsync();
            }

            // Step 3C: Total (unfiltered) count query
            var totalQuery = $"SELECT COUNT(*) FROM [{dbName}].[{schema}].[{table}]";
            using (var totalCmd = new SqlCommand(totalQuery, conn))
            {
                totalCount = (int)await totalCmd.ExecuteScalarAsync();
            }

            return (dt, filteredCount, totalCount);
        }
        public async Task<string> GenerateViewsScriptAsync(string dbName, string viewName, string format)
        {
            var builder = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = dbName,
                MultipleActiveResultSets = true
            };

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            // Split schema and view name safely
            var parts = viewName.Contains('.') ? viewName.Split('.') : new[] { "dbo", viewName };
            var schema = parts[0];
            var view = parts[1];
            string schemaView = $"{schema}.{view}";

            string? viewDefinition = null;
            var indexes = new Dictionary<string, List<string>>();

            // 1️⃣ Get View Definition
            const string defQuery = @"
SELECT m.definition
FROM sys.sql_modules m
INNER JOIN sys.views v ON m.object_id = v.object_id
INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
WHERE s.name = @Schema AND v.name = @ViewName;";

            using (var defCmd = new SqlCommand(defQuery, conn))
            {
                defCmd.Parameters.AddWithValue("@Schema", schema);
                defCmd.Parameters.AddWithValue("@ViewName", view);
                viewDefinition = (string?)await defCmd.ExecuteScalarAsync();
            }

            if (string.IsNullOrEmpty(viewDefinition))
                return $"-- View [{schemaView}] not found in database [{dbName}].";

            // 🧹 Clean up: remove any extra CREATE/ALTER VIEW header
            viewDefinition = System.Text.RegularExpressions.Regex.Replace(
                viewDefinition,
                @"(?is)^\s*(CREATE|ALTER)\s+VIEW\s+\[?\w+\]?(?:\.\[?\w+\]?)?\s+AS\s+",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ).Trim();

            // 2️⃣ Get Indexed View Indexes
            const string indexQuery = @"
SELECT i.name AS IndexName, c.name AS ColumnName
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.views v ON i.object_id = v.object_id
JOIN sys.schemas s ON v.schema_id = s.schema_id
WHERE v.name = @ViewName AND s.name = @Schema
AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
AND i.type_desc IN ('CLUSTERED', 'NONCLUSTERED');";

            using (var iCmd = new SqlCommand(indexQuery, conn))
            {
                iCmd.Parameters.AddWithValue("@Schema", schema);
                iCmd.Parameters.AddWithValue("@ViewName", view);

                using var reader = await iCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string idxName = reader.GetString(0);
                    string colName = reader.GetString(1);

                    if (!indexes.ContainsKey(idxName))
                        indexes[idxName] = new List<string>();

                    indexes[idxName].Add(colName);
                }
            }

            // 3️⃣ Script Builder
            var sb = new StringBuilder();
            sb.AppendLine($"USE [{dbName}];");
            sb.AppendLine("GO");
            sb.AppendLine();

            switch (format)
            {
                case "create-script":
                    sb.AppendLine($"CREATE VIEW [{schema}].[{view}] AS");
                    sb.AppendLine(viewDefinition);
                    sb.AppendLine("GO");
                    break;

                case "alter-script":
                    sb.AppendLine($"ALTER VIEW [{schema}].[{view}] AS");
                    sb.AppendLine(viewDefinition);
                    sb.AppendLine("GO");
                    break;

                case "create-alter-script":
                    sb.AppendLine($"CREATE OR ALTER VIEW [{schema}].[{view}] AS");
                    sb.AppendLine(viewDefinition);
                    sb.AppendLine("GO");
                    break;

                case "drop-script":
                    sb.AppendLine($"DROP VIEW IF EXISTS [{schema}].[{view}];");
                    sb.AppendLine("GO");
                    break;

                case "drop-create-script":
                    sb.AppendLine($"DROP VIEW IF EXISTS [{schema}].[{view}];");
                    sb.AppendLine("GO");
                    sb.AppendLine();
                    sb.AppendLine($"CREATE VIEW [{schema}].[{view}] AS");
                    sb.AppendLine(viewDefinition);
                    sb.AppendLine("GO");
                    break;

                case "indexes-script":
                    if (indexes.Count == 0)
                    {
                        sb.AppendLine($"-- No indexes found on view [{schemaView}].");
                    }
                    else
                    {
                        foreach (var kv in indexes)
                        {
                            sb.AppendLine($"CREATE INDEX [{kv.Key}] ON [{schema}].[{view}] ({string.Join(", ", kv.Value.Select(c => $"[{c}]"))});");
                        }
                        sb.AppendLine("GO");
                    }
                    break;

                default:
                    sb.AppendLine("-- Unsupported format for view script.");
                    break;
            }

            return sb.ToString();
        }




    }
}
