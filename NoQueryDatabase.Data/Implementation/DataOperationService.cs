using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Configuration;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.TableExplorerModel;
using NoQueryDatabase.Utility;

namespace NoQueryDatabase.Data.Implementation
{
    public class DataOperationService : IDataOperationService
    {
        private readonly string _masterConnectionString;
        private readonly IMetadataProvider _metadataProvider;

        public DataOperationService(IConfiguration config, IMetadataProvider metadataProvider)
        {
            _masterConnectionString = config.GetConnectionString("MasterDB");
            _metadataProvider = metadataProvider;
        }




        public async Task<(DataTable, int, int, Dictionary<string, string>)> GetTableDataAsync(
            string serverName,
            string dbName,
            string tableName,
            int page,
            int pageSize,
            string filterColumn = null,
            string filterValue = null,
            string sortOrder = "DESC",
            string filterOperator = "LIKE")
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

            var offset = (page - 1) * pageSize;
            string op = (filterOperator ?? "LIKE").ToUpperInvariant();
            bool isNullCheck = op == "IS NULL" || op == "IS NOT NULL";
            bool hasFilterColumn = !string.IsNullOrWhiteSpace(filterColumn);

            string whereClause = "";
            bool bindFilterValue = false;
            string paramFilterValue = null;

            if (hasFilterColumn)
            {
                if (op == "IS NULL")
                {
                    whereClause = $"WHERE ([{filterColumn}] IS NULL OR [{filterColumn}] = '')";
                }
                else if (op == "IS NOT NULL")
                {
                    whereClause = $"WHERE ([{filterColumn}] IS NOT NULL AND [{filterColumn}] <> '')";
                }
                else if (!string.IsNullOrWhiteSpace(filterValue))
                {
                    whereClause = $"WHERE [{filterColumn}] {op} @FilterValue";
                    bindFilterValue = true;
                    paramFilterValue = op switch
                    {
                        "LIKE" or "NOT LIKE" => $"%{filterValue}%",
                        _ => filterValue
                    };
                }
            }

            string orderDirection = string.IsNullOrEmpty(sortOrder)
     ? "DESC"
     : (sortOrder.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC");
            string orderByColumn = hasFilterColumn ? $"[{filterColumn}]" : "(SELECT NULL)";

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            var parts = tableName.Split('.');
            var schema = parts[0];
            var table = parts[1];

            // 1️⃣ Total unfiltered count
            var totalCountQuery = $"SELECT COUNT(*) FROM [{dbName}].[{schema}].[{table}]";
            using var totalCmd = new SqlCommand(totalCountQuery, conn);
            var totalCount = (int)await totalCmd.ExecuteScalarAsync();

            // 2️⃣ Filtered count
            var filteredCountQuery = $"SELECT COUNT(*) FROM [{dbName}].[{schema}].[{table}] {whereClause}";
            using var filteredCmd = new SqlCommand(filteredCountQuery, conn);
            if (bindFilterValue)
                filteredCmd.Parameters.AddWithValue("@FilterValue", paramFilterValue);
            var filteredCount = (int)await filteredCmd.ExecuteScalarAsync();

            string getOrderColumnQuery = $@"
DECLARE @orderByColumn NVARCHAR(128);

SELECT TOP 1 @orderByColumn = name
FROM sys.columns
WHERE object_id = OBJECT_ID('[{dbName}].[{schema}].[{table}]')
  AND is_identity = 1;

IF @orderByColumn IS NULL
BEGIN
    SELECT TOP 1 @orderByColumn = name
    FROM sys.columns
    WHERE object_id = OBJECT_ID('[{dbName}].[{schema}].[{table}]')
    ORDER BY column_id;
END

SELECT @orderByColumn;
";

            // Execute this query first and get the column name
            using var orderByColumnDefaultCmd = new SqlCommand(getOrderColumnQuery, conn);
            var orderByColumnDefault = await orderByColumnDefaultCmd.ExecuteScalarAsync();
            // 3️⃣ Paged data query
            var dataQuery = string.IsNullOrEmpty(sortOrder) ? $@"
                SELECT * FROM (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY {orderByColumnDefault} {orderDirection}) AS RowNum
                    FROM [{dbName}].[{schema}].[{table}]
                    {whereClause}
                ) AS RowConstrainedResult
                WHERE RowNum > @Offset AND RowNum <= (@Offset + @PageSize) ORDER BY 1 {orderDirection}" : $@"
                SELECT * FROM (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY {orderByColumn} {orderDirection}) AS RowNum
                    FROM [{dbName}].[{schema}].[{table}]
                    {whereClause}
                ) AS RowConstrainedResult
                WHERE RowNum > @Offset AND RowNum <= (@Offset + @PageSize)";

            using var dataCmd = new SqlCommand(dataQuery, conn);
            dataCmd.Parameters.AddWithValue("@Offset", offset);
            dataCmd.Parameters.AddWithValue("@PageSize", pageSize);
            if (bindFilterValue)
                dataCmd.Parameters.AddWithValue("@FilterValue", paramFilterValue);

            var dt = new DataTable();
            using var reader = await dataCmd.ExecuteReaderAsync();
            dt.Load(reader);

            // 4️⃣ Column types
            var columnTypes = await _metadataProvider.GetMetadataAsync(serverName, dbName, $"TableColumnTypes_{tableName}", async () =>
            {
                var types = new Dictionary<string, string>();
                var typeQuery = @"
        SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = @Table
          AND TABLE_SCHEMA = @Schema";

                // We need a separate connection if the datareader is still open, or just reuse the existing one since DataReader for `dt` is closed.
                using var typeCmd = new SqlCommand(typeQuery, conn);
                typeCmd.Parameters.AddWithValue("@Table", table);
                typeCmd.Parameters.AddWithValue("@Schema", schema);
                using var typeReader = await typeCmd.ExecuteReaderAsync();
                while (await typeReader.ReadAsync())
                {
                    var column = typeReader.GetString(0);
                    var dataType = typeReader.GetString(1);
                    var length = typeReader.IsDBNull(2) ? null : typeReader.GetValue(2)?.ToString();

                    var fullType = (dataType == "varchar" || dataType == "nvarchar" || dataType == "char")
                        ? $"{dataType}({(length == "-1" ? "MAX" : length)})"
                        : dataType;

                    types[column] = fullType;
                }
                return types;
            });

            return (dt, filteredCount, totalCount, columnTypes);
        }
        public async Task<bool> UpdateTableRowAsync(TableRowUpdateRequest updateTableRequest)
        {
            var connKey = $"{updateTableRequest.ServerName}_{updateTableRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);

            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {updateTableRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = updateTableRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows"
            };

            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            var parts = updateTableRequest.TableName.Split('.');
            var schema = parts[0];
            var table = parts[1];
            // ✅ Detect identity column dynamically
            string getIdentityColQuery = @"
SELECT TOP 1 name
FROM sys.columns
WHERE object_id = OBJECT_ID(@SchemaTable)
  AND is_identity = 1;";

            using var cmdIdentity = new SqlCommand(getIdentityColQuery, conn);
            cmdIdentity.Parameters.AddWithValue("@SchemaTable", $"[{schema}].[{table}]");

            var identityCol = await cmdIdentity.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(identityCol))
            {
                // If no identity column, take first column
                getIdentityColQuery = $@"
            SELECT TOP 1 name 
            FROM sys.columns 
            WHERE object_id = OBJECT_ID('[{updateTableRequest.DbName}].[{schema}].[{table}]')
            ORDER BY column_id;";
                using var cmdFirstCol = new SqlCommand(getIdentityColQuery, conn);
                identityCol = await cmdFirstCol.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrEmpty(identityCol))
                throw new Exception("Unable to determine primary/identity column for update.");

            // ✅ Build UPDATE query dynamically
            if (!updateTableRequest.RowData.ContainsKey(identityCol))
                throw new Exception($"Missing identity/primary key column '{identityCol}' in updated data.");

            string whereClause = $"WHERE [{identityCol}] = @{identityCol}";
            var setClauses = updateTableRequest.RowData
                .Where(kvp => kvp.Key != identityCol)
                .Select(kvp => $"[{kvp.Key}] = @{kvp.Key}")
                .ToList();

            if (!setClauses.Any())
                throw new Exception("No updatable columns found.");

            string updateQuery = $@"
UPDATE [{updateTableRequest.DbName}].[{schema}].[{table}]
SET {string.Join(", ", setClauses)}
{whereClause};";

            using var updateCmd = new SqlCommand(updateQuery, conn);
            foreach (var kvp in updateTableRequest.RowData)
                updateCmd.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? (object)DBNull.Value);

            int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        public async Task<bool> DeleteTableRowAsync(TableRowDeleteRequest tableRowDeleteRequest)
        {
            // 1️⃣ Get dynamic connection
            var connKey = $"{tableRowDeleteRequest.ServerName}_{tableRowDeleteRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);
            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {tableRowDeleteRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = tableRowDeleteRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };

            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            // 2️⃣ Split schema and table
            var parts = tableRowDeleteRequest.TableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : parts[0];

            // 3️⃣ Detect unique column to identify the row
            string pkColumn = await new SqlCommand(@"
        SELECT TOP 1 c.name
        FROM sys.indexes i
        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@SchemaTable)", conn)
            {
                Parameters = { new SqlParameter("@SchemaTable", $"{schema}.{table}") }
            }.ExecuteScalarAsync() as string;

            // Fallback to identity column
            if (string.IsNullOrEmpty(pkColumn))
            {
                pkColumn = await new SqlCommand(@"
            SELECT TOP 1 name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@SchemaTable) AND is_identity = 1", conn)
                {
                    Parameters = { new SqlParameter("@SchemaTable", $"{schema}.{table}") }
                }.ExecuteScalarAsync() as string;
            }

            // Fallback to first column
            if (string.IsNullOrEmpty(pkColumn))
            {
                pkColumn = await new SqlCommand(@"
            SELECT TOP 1 name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@SchemaTable)
            ORDER BY column_id", conn)
                {
                    Parameters = { new SqlParameter("@SchemaTable", $"{schema}.{table}") }
                }.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrEmpty(pkColumn))
                throw new Exception("No column found to identify the row.");

            // 4️⃣ Get the value from RowData
            if (!tableRowDeleteRequest.RowData.TryGetValue(pkColumn, out var pkValue))
                throw new Exception($"Missing value for column '{pkColumn}' in RowData.");

            object columnValue = pkValue ?? DBNull.Value;

            // 5️⃣ Convert value to correct column type
            var colTypeQuery = @"
        SELECT DATA_TYPE 
        FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Column";
            string colType;
            using (var colTypeCmd = new SqlCommand(colTypeQuery, conn))
            {
                colTypeCmd.Parameters.AddWithValue("@Schema", schema);
                colTypeCmd.Parameters.AddWithValue("@Table", table);
                colTypeCmd.Parameters.AddWithValue("@Column", pkColumn);
                colType = await colTypeCmd.ExecuteScalarAsync() as string;
            }

            if (columnValue is JsonElement jsonElement)
            {
                switch (jsonElement.ValueKind)
                {
                    case JsonValueKind.String:
                        columnValue = jsonElement.GetString();
                        break;
                    case JsonValueKind.Number:
                        if (jsonElement.TryGetInt32(out int intVal))
                            columnValue = intVal;
                        else if (jsonElement.TryGetInt64(out long longVal))
                            columnValue = longVal;
                        else if (jsonElement.TryGetDecimal(out decimal decVal))
                            columnValue = decVal;
                        else
                            columnValue = jsonElement.GetDouble();
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        columnValue = jsonElement.GetBoolean();
                        break;
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        columnValue = DBNull.Value;
                        break;
                    default:
                        columnValue = jsonElement.ToString();
                        break;
                }
            }

            // Now safely handle SQL type conversion
            if (colType != null && columnValue != DBNull.Value)
            {
                if (colType.Contains("int", StringComparison.OrdinalIgnoreCase))
                    columnValue = Convert.ToInt32(columnValue);
                else if (colType.Contains("bigint", StringComparison.OrdinalIgnoreCase))
                    columnValue = Convert.ToInt64(columnValue);
                else if (colType.Contains("decimal", StringComparison.OrdinalIgnoreCase) ||
                         colType.Contains("numeric", StringComparison.OrdinalIgnoreCase))
                    columnValue = Convert.ToDecimal(columnValue);
            }

            // 6️⃣ Validate row exists
            string checkQuery = $"SELECT COUNT(*) FROM [{schema}].[{table}] WHERE [{pkColumn}] = @Value";
            using var checkCmd = new SqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("@Value", columnValue);
            int count = (int)await checkCmd.ExecuteScalarAsync();
            if (count == 0)
                throw new Exception($"No row found with {pkColumn} = {columnValue} in table {schema}.{table}");

            // 7️⃣ Delete the row
            string deleteQuery = $"DELETE FROM [{schema}].[{table}] WHERE [{pkColumn}] = @Value";
            using var deleteCmd = new SqlCommand(deleteQuery, conn);
            deleteCmd.Parameters.AddWithValue("@Value", columnValue);
            var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();

            return rowsAffected > 0;
        }

        public async Task<bool> InsertTableRowAsync(TableRowInsertRequest tableRowInsertRequest)
        {
            // Get the dynamic connection
            var connKey = $"{tableRowInsertRequest.ServerName}_{tableRowInsertRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);
            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {tableRowInsertRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = tableRowInsertRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };
            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            // Split schema and table
            var parts = tableRowInsertRequest.TableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : parts[0];

            // Step 1: Detect identity column
            string identityColQuery = @"
        SELECT TOP 1 name
        FROM sys.columns
        WHERE object_id = OBJECT_ID(@SchemaTable)
          AND is_identity = 1;";

            using var cmdId = new SqlCommand(identityColQuery, conn);
            cmdId.Parameters.AddWithValue("@SchemaTable", $"[{schema}].[{table}]");
            var identityCol = await cmdId.ExecuteScalarAsync() as string;

            // Step 2: Prepare insert query
            var columns = string.Join(", ", tableRowInsertRequest.RowData.Keys.Select(k => $"[{k}]"));
            var parameters = string.Join(", ", tableRowInsertRequest.RowData.Keys.Select(k => $"@{k}"));
            var query = $"INSERT INTO [{schema}].[{table}] ({columns}) VALUES ({parameters})";

            try
            {
                // Step 3: Enable IDENTITY_INSERT if identity column exists and is provided
                if (!string.IsNullOrEmpty(identityCol) && tableRowInsertRequest.RowData.ContainsKey(identityCol))
                {
                    await new SqlCommand($"SET IDENTITY_INSERT [{schema}].[{table}] ON;", conn).ExecuteNonQueryAsync();
                }

                using var cmd = new SqlCommand(query, conn);

                // Step 4: Add parameters
                foreach (var kvp in tableRowInsertRequest.RowData)
                {
                    object value = kvp.Value ?? DBNull.Value;

                    // Handle JSON elements if necessary
                    if (value is JsonElement jsonElement)
                    {
                        value = jsonElement.ValueKind switch
                        {
                            JsonValueKind.String => jsonElement.GetString(),
                            JsonValueKind.Number => jsonElement.TryGetInt32(out int intVal) ? intVal :
                                                    jsonElement.TryGetInt64(out long longVal) ? longVal :
                                                    jsonElement.TryGetDecimal(out decimal decVal) ? decVal :
                                                    jsonElement.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
                            _ => jsonElement.ToString()
                        };
                    }

                    cmd.Parameters.AddWithValue($"@{kvp.Key}", value);
                }

                // Step 5: Execute insert
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                return rowsAffected > 0;
            }
            finally
            {
                // Step 6: Turn off IDENTITY_INSERT if it was enabled
                if (!string.IsNullOrEmpty(identityCol) && tableRowInsertRequest.RowData.ContainsKey(identityCol))
                {
                    await new SqlCommand($"SET IDENTITY_INSERT [{schema}].[{table}] OFF;", conn).ExecuteNonQueryAsync();
                }
            }
        }
        public async Task<string> TruncateTableAsync(TableTruncateRequest tableTruncateRequest)
        {
            // Get the dynamic connection
            var connKey = $"{tableTruncateRequest.ServerName}_{tableTruncateRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);
            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {tableTruncateRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = tableTruncateRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };
            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            // Split schema and table
            var parts = tableTruncateRequest.TableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : parts[0];

            string sql = $"TRUNCATE TABLE {table};";
            using (var cmd = new SqlCommand(sql, conn))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            return $"Table '{table}' truncated successfully.";
        }

        public async Task<string> DropTableAsync(TableDropRequest tableDropRequest)
        {
            // Get the dynamic connection
            var connKey = $"{tableDropRequest.ServerName}_{tableDropRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);
            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {tableDropRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = tableDropRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };
            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            // Split schema and table
            var parts = tableDropRequest.TableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : parts[0];

            string sql = $"DROP TABLE {table};";
            using (var cmd = new SqlCommand(sql, conn))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            return $"Table '{table}' dropped successfully.";
        }



        public async Task<(DataTable, int, int)> SearchTableDataAsync(
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

        public async Task<List<DataTable>> ExportTableDataAsync(
    string dbName,
    string tableName,
    string keyword = "",
    string filterColumn = "",
    string filterOperator = "LIKE",
    string filterValue = "",
    List<string> visibleColumns = null,
    string sortOrder = "",
    int chunkSize = 50)
        {
            var builder = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = dbName
            };

            var tables = new List<DataTable>();
            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            var partss = tableName.Split('.');
            var schema = partss[0];
            var table = partss[1];

            // Step 1: Get all column names
            var colQuery = @"
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = @Table
  AND TABLE_SCHEMA = @Schema";

            using var colCmd = new SqlCommand(colQuery, conn);
            colCmd.Parameters.AddWithValue("@Table", table);
            colCmd.Parameters.AddWithValue("@Schema", schema);

            var allColumns = new List<string>();
            using (var reader = await colCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    allColumns.Add(reader.GetString(0));
                }
            }

            if (!allColumns.Any())
                return tables;

            // Step 2: Prepare SELECT clause
            var selectedCols = (visibleColumns != null && visibleColumns.Any())
                ? visibleColumns.Intersect(allColumns, StringComparer.OrdinalIgnoreCase).Select(col => $"[{col}]")
                : allColumns.Select(col => $"[{col}]");

            var selectClause = string.Join(", ", selectedCols);

            // Step 3: Build WHERE clause
            var whereClause = "1=1";
            var parameters = new List<SqlParameter>();

            bool hasColumnFilter = !string.IsNullOrWhiteSpace(filterColumn) &&
                                   !string.IsNullOrWhiteSpace(filterOperator) &&
                                   (!string.IsNullOrWhiteSpace(filterValue) ||
                                    filterOperator == "IS NULL" || filterOperator == "IS NOT NULL");

            if (hasColumnFilter)
            {
                var safeCol = $"[{filterColumn}]";
                if (filterOperator == "IS NULL" || filterOperator == "IS NOT NULL")
                {
                    whereClause += $" AND {safeCol} {filterOperator}";
                }
                else
                {
                    whereClause += $" AND {safeCol} {filterOperator} @FilterValue";
                    parameters.Add(new SqlParameter("@FilterValue", filterValue ?? ""));
                }
            }
            else if (!string.IsNullOrWhiteSpace(keyword))
            {
                var likeParam = $"%{keyword}%";
                var keywordConditions = allColumns.Select(col => $"CAST([{col}] AS NVARCHAR(MAX)) LIKE @Keyword");
                whereClause += $" AND ({string.Join(" OR ", keywordConditions)})";
                parameters.Add(new SqlParameter("@Keyword", likeParam));
            }

            // Step 4: Build ORDER BY
            string orderByClause = "";
            if (!string.IsNullOrWhiteSpace(sortOrder))
            {
                var parts = sortOrder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && allColumns.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                {
                    var direction = parts[1].ToUpper() == "DESC" ? "DESC" : "ASC";
                    orderByClause = $"ORDER BY [{parts[0]}] {direction}";
                }
                else if ((sortOrder.Equals("ASC", StringComparison.OrdinalIgnoreCase) || sortOrder.Equals("DESC", StringComparison.OrdinalIgnoreCase)) &&
                         !string.IsNullOrWhiteSpace(filterColumn) &&
                         allColumns.Contains(filterColumn, StringComparer.OrdinalIgnoreCase))
                {
                    var direction = sortOrder.ToUpper();
                    orderByClause = $"ORDER BY [{filterColumn}] {direction}";
                }
            }

            if (string.IsNullOrWhiteSpace(orderByClause))
                orderByClause = "ORDER BY (SELECT NULL)";

            // Step 5: Fetch in chunks
            int offset = 0;
            while (true)
            {
                var query = $@"
;WITH Filtered AS (
    SELECT {selectClause}, ROW_NUMBER() OVER ({orderByClause}) AS RowNum
    FROM [{dbName}].[{schema}].[{tableName}]
    WHERE {whereClause}
)
SELECT * FROM Filtered
WHERE RowNum > @Offset AND RowNum <= (@Offset + @ChunkSize);";

                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Offset", offset);
                cmd.Parameters.AddWithValue("@ChunkSize", chunkSize);

                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(new SqlParameter(param.ParameterName, param.Value));
                }

                using var reader = await cmd.ExecuteReaderAsync();
                var chunk = new DataTable();
                chunk.Load(reader);

                if (chunk.Rows.Count == 0)
                    break;

                tables.Add(chunk);
                offset += chunkSize;
            }

            return tables;
        }


        

        public async Task<bool> CreateTableAsync(TableCreateRequest tableCreateRequest)
        {
            try
            {
                // Retrieve the dynamic connection info
                var connKey = $"{tableCreateRequest.ServerName}_{tableCreateRequest.DatabaseName}";
                var connectedDb = ConnectionStore.Get(connKey);

                if (connectedDb == null)
                    throw new Exception($"No dynamic connection found for database: {tableCreateRequest.DatabaseName}");

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = connectedDb.ServerName,
                    InitialCatalog = connectedDb.DatabaseName,
                    UserID = connectedDb.Username,
                    Password = connectedDb.Password,
                    TrustServerCertificate = true,
                    Encrypt = false
                };

                using (SqlConnection conn = new SqlConnection(builder.ConnectionString))
                {
                    await conn.OpenAsync();

                    // Optional: Validate that table doesn't already exist before executing
                    if (await TableExistsAsync(conn, tableCreateRequest.SqlQuery))
                        throw new Exception("Table already exists in the database.");

                    using (SqlCommand cmd = new SqlCommand(tableCreateRequest.SqlQuery, conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                // Log exception if you have logger
                Console.WriteLine($"Error in CreateTableAsync: {ex.Message}");
                throw;
            }
        }

        private async Task<bool> TableExistsAsync(SqlConnection conn, string sqlQuery)
        {
            var match = Regex.Match(sqlQuery, @"CREATE\s+TABLE\s+\[?(\w+)\]?", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            var tableName = match.Groups[1].Value;
            var checkQuery = $"IF OBJECT_ID('[dbo].[{tableName}]', 'U') IS NOT NULL SELECT 1 ELSE SELECT 0";
            using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
            {
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 1;
            }
        }

        

        

        

        public async Task<(bool Success, string SqlScript, string Message)> GenerateAlterSqlAsync(
    string serverName,
    string authentication,
    string login,
    string password,
    string connectionString,
    string databaseName,
    string tableName,
    List<ColumnAlterModel> columns)
        {
            // ✅ Build connection string
            var csBuilder = !string.IsNullOrWhiteSpace(connectionString)
                ? new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName }
                : new SqlConnectionStringBuilder
                {
                    DataSource = serverName,
                    InitialCatalog = databaseName,
                    IntegratedSecurity = authentication.Equals("Windows", StringComparison.OrdinalIgnoreCase),
                    UserID = authentication.Equals("SQL", StringComparison.OrdinalIgnoreCase) ? login : null,
                    Password = authentication.Equals("SQL", StringComparison.OrdinalIgnoreCase) ? password : null
                };

            var connStr = csBuilder.ToString();
            var sqlStatements = new List<string>();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // 🔹 Fetch current table schema
            var existingCols = new Dictionary<string, (string DataType, int? MaxLen, string Nullable, string DefaultVal, bool IsPK, bool IsIdentity)>();
            const string schemaSql = @"
        SELECT 
            c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, 
            c.IS_NULLABLE, c.COLUMN_DEFAULT,
            COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
            CASE WHEN k.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT ku.TABLE_NAME, ku.COLUMN_NAME 
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc 
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku 
              ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME 
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND ku.TABLE_NAME = @TableName
        ) k ON c.COLUMN_NAME = k.COLUMN_NAME
        WHERE c.TABLE_NAME = @TableName;";

            await using (var cmd = new SqlCommand(schemaSql, conn))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    existingCols[rdr["COLUMN_NAME"].ToString()] = (
                        rdr["DATA_TYPE"].ToString(),
                        rdr["CHARACTER_MAXIMUM_LENGTH"] as int?,
                        rdr["IS_NULLABLE"].ToString(),
                        rdr["COLUMN_DEFAULT"]?.ToString(),
                        Convert.ToInt32(rdr["IsPrimaryKey"]) == 1,
                        Convert.ToBoolean(rdr["IsIdentity"])
                    );
                }
            }

            // 🔹 Loop through columns
            foreach (var col in columns)
            {
                string name = col.Name;
                string type = col.DataType?.Trim() ?? "";
                string len = string.IsNullOrWhiteSpace(col.Length) ? "" : col.Length;
                string nullable = col.Nullable == "NO" ? "NOT NULL" : "";
                bool exists = existingCols.ContainsKey(name);
                var orig = exists ? existingCols[name] : default;

                switch (col.Action.ToUpper())
                {
                    case "DROP":
                        sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] DROP COLUMN [{name}];");
                        break;

                    case "MODIFY":
                        if (!exists)
                            continue;

                        if (!string.IsNullOrWhiteSpace(col.NewName) && col.NewName != name)
                            sqlStatements.Add($"EXEC sp_rename 'dbo.{tableName}.{name}', '{col.NewName}', 'COLUMN';");

                        // Detect IDENTITY alteration
                        if (orig.IsIdentity != col.IsIdentity)
                        {
                            sqlStatements.Add($"-- ⚠ Cannot directly alter IDENTITY for existing column '{name}' — requires rebuild.");
                            continue;
                        }

                        // Detect type/nullable change
                        bool isTypeChanged = !string.Equals(orig.DataType, type, StringComparison.OrdinalIgnoreCase)
                                             || (orig.MaxLen?.ToString() ?? "") != len
                                             || (orig.Nullable == "NO" ? "NOT NULL" : "") != nullable;

                        if (isTypeChanged)
                        {
                            string typeSpec = len.Length > 0 &&
                                              new[] { "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "DECIMAL", "NUMERIC" }
                                              .Any(t => type.ToUpper().Contains(t))
                                ? $"{type}({len})"
                                : type;

                            sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ALTER COLUMN [{name}] {typeSpec} {nullable};");
                        }

                        // Handle PK change
                        if (orig.IsPK != col.IsPrimaryKey)
                        {
                            if (orig.IsPK)
                            {
                                sqlStatements.Add($@"
DECLARE @pkName NVARCHAR(128);
SELECT @pkName = kc.name
FROM sys.key_constraints kc
JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE kc.parent_object_id = OBJECT_ID('dbo.{tableName}') AND c.name = '{name}' AND kc.type = 'PK';
IF @pkName IS NOT NULL EXEC('ALTER TABLE [dbo].[{tableName}] DROP CONSTRAINT ' + @pkName);
");
                            }
                            else
                            {
                                sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT PK_{tableName}_{name} PRIMARY KEY ([{name}]);");
                            }
                        }

                        // Default constraint changes
                        if (orig.DefaultVal != col.DefaultValue)
                        {
                            sqlStatements.Add($@"
DECLARE @dfName NVARCHAR(128);
SELECT @dfName = dc.name FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE OBJECT_NAME(dc.parent_object_id) = '{tableName}' AND c.name = '{name}';
IF @dfName IS NOT NULL EXEC('ALTER TABLE [dbo].[{tableName}] DROP CONSTRAINT ' + @dfName);");

                            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                            {
                                string formatted = Regex.IsMatch(col.DefaultValue, @"^[0-9]+$") ? col.DefaultValue : $"'{col.DefaultValue}'";
                                sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT DF_{tableName}_{name} DEFAULT ({formatted}) FOR [{name}];");
                            }
                        }
                        break;

                    case "ADD":
                        string typeDef = len.Length > 0 &&
                                         new[] { "VARCHAR", "NVARCHAR", "CHAR", "DECIMAL", "NUMERIC" }
                                         .Any(t => type.ToUpper().Contains(t))
                            ? $"{type}({len})"
                            : type;

                        string identity = col.IsIdentity ? "IDENTITY(1,1)" : "";

                        // ✅ Handle IDENTITY ADD case
                        if (col.IsIdentity)
                        {
                            sqlStatements.Add($@"
-- ⚙ Identity column detected: '{name}' → rebuilding table
DECLARE @TempTable NVARCHAR(200) = '{tableName}_Temp_' + CONVERT(VARCHAR(14), GETDATE(), 112) + REPLACE(CONVERT(VARCHAR(8), GETDATE(), 108), ':', '');
DECLARE @SQL NVARCHAR(MAX) = '';

-- Step 1: Create new table with same structure + new identity column
SET @SQL = 'SELECT TOP 0 * INTO ' + @TempTable + ' FROM [dbo].[{tableName}]; 
ALTER TABLE ' + @TempTable + ' ADD [{name}] {typeDef} IDENTITY(1,1);';

EXEC(@SQL);

-- Step 2: Copy data
SET @SQL = 'INSERT INTO ' + @TempTable + ' (' + 
            STUFF((SELECT ' + [' + COLUMN_NAME + ']' FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' FOR XML PATH('')), 1, 2, '') + ') 
            SELECT * FROM [dbo].[{tableName}];';
EXEC(@SQL);

-- Step 3: Drop old table and rename new
SET @SQL = 'DROP TABLE [dbo].[{tableName}];
EXEC sp_rename ''' + @TempTable + ''', ''{tableName}'';';
EXEC(@SQL);

PRINT '✅ Table rebuilt with IDENTITY column [{name}]';
");
                            break;
                        }

                        sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ADD [{name}] {typeDef} {identity} {nullable};");

                        if (col.IsPrimaryKey)
                            sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT PK_{tableName}_{name} PRIMARY KEY ([{name}]);");

                        if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                        {
                            string formattedDef = Regex.IsMatch(col.DefaultValue, @"^[0-9]+$") ? col.DefaultValue : $"'{col.DefaultValue}'";
                            sqlStatements.Add($"ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT DF_{tableName}_{name} DEFAULT ({formattedDef}) FOR [{name}];");
                        }
                        break;
                }
            }

            bool hasChanges = sqlStatements.Count > 0;
            string finalSql = hasChanges ? string.Join("\n", sqlStatements) : "-- No changes detected.";
            return (hasChanges, finalSql, hasChanges ? "SQL generated successfully." : "No changes detected.");
        }



        public async Task<(bool Success, List<string> Messages)> ExecuteAlterTableAsync(
    string serverName, string authentication, string login, string password, string connectionString, string databaseName,
    string sqlQuery)
        {
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(sqlQuery)) throw new ArgumentException("Invalid input parameters.");
            // --- Build connection string ---
            string connStr; if (!string.IsNullOrWhiteSpace(connectionString)) { var csBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName }; connStr = csBuilder.ToString(); } else { var builder = new SqlConnectionStringBuilder { DataSource = serverName, InitialCatalog = databaseName, IntegratedSecurity = authentication.Equals("Windows", StringComparison.OrdinalIgnoreCase) }; if (authentication.Equals("SQL", StringComparison.OrdinalIgnoreCase)) { builder.UserID = login; builder.Password = password; } connStr = builder.ToString(); }
            var messages = new List<string>(); await using var conn = new SqlConnection(connStr); await conn.OpenAsync();

            var statements = Regex.Split(sqlQuery, @"^\s*GO\s*$|;", RegexOptions.IgnoreCase | RegexOptions.Multiline)
                                  .Select(s => s.Trim())
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .ToList();

            //foreach (var statement in statements)
            //{
            //    try
            //    {
            //        await using var transaction = await conn.BeginTransactionAsync();
            //        await using var cmd = new SqlCommand(statement, conn, (SqlTransaction)transaction)
            //        {
            //            CommandTimeout = 600
            //        };
            //        await cmd.ExecuteNonQueryAsync();
            //        await transaction.CommitAsync();

            //        messages.Add($"✅ Success: {GetStatementSummary(statement)}");
            //    }
            //    catch (SqlException ex)
            //    {
            //        messages.Add($"❌ Failed: {GetStatementSummary(statement)} — {ex.Message}");
            //    }
            //}
            try
            {
                await using var transaction = await conn.BeginTransactionAsync();
                await using var cmd = new SqlCommand(sqlQuery, conn, (SqlTransaction)transaction)
                {
                    CommandTimeout = 600
                };
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();

                messages.Add($"✅ Success: {GetStatementSummary(sqlQuery)}");
            }
            catch (SqlException ex)
            {
                messages.Add($"❌ Failed: {GetStatementSummary(sqlQuery)} — {ex.Message}");
            }

            bool allSucceeded = messages.All(m => m.StartsWith("✅"));
            return (allSucceeded, messages);
        }

        private static string GetStatementSummary(string sql)
        {
            var clean = sql.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length > 80 ? clean.Substring(0, 80) + "..." : clean;
        }




    }
}
