using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.VisualBasic;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.StoredProcedureExplorerModel;
using NoQueryDatabase.Utility;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NoQueryDatabase.Data.Implementation
{
    public class StoredProcedureExplorerDataService:IStoredProcedureExplorerDataService
    {
        public StoredProcedureExplorerDataService()
        {
        }
        public async Task<List<string>> GetStoredProcedureParameters(string storedProcedureName, string serverName, string dbName)
        {
            var parameters = new List<string>();

            try
            {
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

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();

                    // ✅ Split schema and proc name (handle dbo.ProcName format)
                    string schemaName = "dbo";
                    string procName = storedProcedureName;

                    if (storedProcedureName.Contains('.'))
                    {
                        var parts = storedProcedureName.Split('.');
                        schemaName = parts[0];
                        procName = parts[1];
                    }

                    string query = @"
                SELECT 
                    PARAMETER_NAME,
                    DATA_TYPE,
                    COALESCE(
                        CASE 
                            WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL AND CHARACTER_MAXIMUM_LENGTH > 0 
                                THEN '(' + 
                                    CASE 
                                        WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX'
                                        ELSE CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR)
                                    END + ')'
                            WHEN NUMERIC_PRECISION IS NOT NULL 
                                THEN '(' + CAST(NUMERIC_PRECISION AS VARCHAR) + 
                                     COALESCE(',' + CAST(NUMERIC_SCALE AS VARCHAR), '') + ')'
                        END, ''
                    ) AS TYPE_DETAILS
                FROM INFORMATION_SCHEMA.PARAMETERS
                WHERE SPECIFIC_NAME = @ProcName
                  AND SPECIFIC_SCHEMA = @SchemaName
                ORDER BY ORDINAL_POSITION;";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ProcName", procName);
                        command.Parameters.AddWithValue("@SchemaName", schemaName);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string paramName = reader["PARAMETER_NAME"].ToString();
                                string dataType = reader["DATA_TYPE"].ToString();
                                string typeDetails = reader["TYPE_DETAILS"].ToString();

                                parameters.Add($"{paramName} {dataType}{typeDetails}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                parameters.Add($"Error: {ex.Message}");
            }

            return parameters;
        }


        public async Task<(DataTable, int, Dictionary<string, string>, long, long, long, List<int>)> ExecuteStoredProcedure(
    StoredProcedureExecuteRequest storedProcedureExecuteRequest)
        {
            var pagedDt = new DataTable();
            var columnTypes = new Dictionary<string, string>();
            int totalCount = 0;
            long executionTimeMs = 0;
            long minimumTimeMs = long.MaxValue;
            long maximumTimeMs = long.MinValue;
            List<int> listexecutionTimeMs = new List<int>();

            try
            {
                var connKey = $"{storedProcedureExecuteRequest.ServerName}_{storedProcedureExecuteRequest.DbName}";
                var connectedDb = ConnectionStore.Get(connKey);

                if (connectedDb == null)
                    throw new Exception($"No dynamic connection found for database: {storedProcedureExecuteRequest.DbName}");

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = connectedDb.ServerName,
                    InitialCatalog = storedProcedureExecuteRequest.DbName,
                    IntegratedSecurity = connectedDb.Authentication == "Windows",
                };

                if (connectedDb.Authentication == "SQL")
                {
                    builder.UserID = connectedDb.Username;
                    builder.Password = connectedDb.Password;
                }

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                long totalElapsed = 0;

                for (int i = 0; i < storedProcedureExecuteRequest.ExecutionCount; i++)
                {
                    using var command = new SqlCommand(storedProcedureExecuteRequest.ObjectName, connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    // Add parameters
                    foreach (var param in storedProcedureExecuteRequest.Parameters)
                    {
                        var cleanKey = param.Key.Split(' ')[0];

                        object cleanValue;
                        if (param.Value is JsonElement jsonElement)
                        {
                            switch (jsonElement.ValueKind)
                            {
                                case JsonValueKind.Number:
                                    if (jsonElement.TryGetInt64(out long longVal))
                                        cleanValue = longVal;
                                    else
                                        cleanValue = jsonElement.GetDecimal();
                                    break;
                                case JsonValueKind.String:
                                    cleanValue = jsonElement.GetString();
                                    break;
                                case JsonValueKind.True:
                                case JsonValueKind.False:
                                    cleanValue = jsonElement.GetBoolean();
                                    break;
                                case JsonValueKind.Null:
                                    cleanValue = DBNull.Value;
                                    break;
                                default:
                                    cleanValue = jsonElement.ToString();
                                    break;
                            }
                        }
                        else
                        {
                            cleanValue = param.Value ?? DBNull.Value;
                        }

                        command.Parameters.AddWithValue(cleanKey, cleanValue);
                    }

                    var startTime = DateTime.Now;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (i == 0) // Only load data on first run
                        {
                            // Initialize columns dynamically
                            for (int colIndex = 0; colIndex < reader.FieldCount; colIndex++)
                            {
                                var type = reader.GetFieldType(colIndex) ?? typeof(object);
                                // Ensure unique column names by appending index if needed
                                string colName = reader.GetName(colIndex);
                                if (string.IsNullOrWhiteSpace(colName)) colName = $"Column{colIndex + 1}";
                                if (pagedDt.Columns.Contains(colName)) colName += $"_{colIndex}";
                                
                                pagedDt.Columns.Add(colName, type);
                            }

                            int rowIndex = 0;
                            int skip = (storedProcedureExecuteRequest.Page - 1) * storedProcedureExecuteRequest.PageSize;
                            int pageSize = storedProcedureExecuteRequest.PageSize;

                            while (await reader.ReadAsync())
                            {
                                if (rowIndex >= skip && rowIndex < skip + pageSize)
                                {
                                    DataRow row = pagedDt.NewRow();
                                    for (int colIndex = 0; colIndex < reader.FieldCount; colIndex++)
                                    {
                                        row[colIndex] = reader.GetValue(colIndex);
                                    }
                                    pagedDt.Rows.Add(row);
                                }
                                rowIndex++;
                            }
                            totalCount = rowIndex;
                        }
                        else
                        {
                            while (await reader.ReadAsync()) { } // Just iterate to simulate load
                        }
                    }

                    var endTime = DateTime.Now;
                    long elapsed = (long)(endTime - startTime).TotalMilliseconds;

                    // Add execution time to list
                    listexecutionTimeMs.Add((int)elapsed);

                    totalElapsed += elapsed;
                    if (elapsed < minimumTimeMs) minimumTimeMs = elapsed;
                    if (elapsed > maximumTimeMs) maximumTimeMs = elapsed;
                }

                // Average execution time
                executionTimeMs = totalElapsed / storedProcedureExecuteRequest.ExecutionCount;

                // Extract column names and data types (using pagedDt instead of dt)
                foreach (DataColumn col in pagedDt.Columns)
                {
                    var typeName = col.DataType.Name;
                    if (col.DataType == typeof(string))
                        typeName = $"nvarchar({(col.MaxLength == int.MaxValue ? "MAX" : col.MaxLength)})";
                    else if (col.DataType == typeof(int))
                        typeName = "int";
                    else if (col.DataType == typeof(long))
                        typeName = "bigint";
                    else if (col.DataType == typeof(DateTime))
                        typeName = "datetime";
                    else if (col.DataType == typeof(decimal))
                        typeName = "decimal";
                    else if (col.DataType == typeof(bool))
                        typeName = "bit";

                    columnTypes[col.ColumnName] = typeName;
                }

                // Handle case if loop never ran (safety)
                if (minimumTimeMs == long.MaxValue) minimumTimeMs = 0;
                if (maximumTimeMs == long.MinValue) maximumTimeMs = 0;

                return (pagedDt, totalCount, columnTypes, executionTimeMs, minimumTimeMs, maximumTimeMs, listexecutionTimeMs);
            }
            catch
            {
                throw;
            }
        }



        public async Task<(DataTable, int, Dictionary<string, string>, long, long, long, List<int>)> ExecuteSearchStoredProcedure(StoredProcedureExecuteRequest storedProcedureExecuteRequest)
        {
            var pagedDt = new DataTable();
            var columnTypes = new Dictionary<string, string>();
            int totalCount = 0;
            long executionTimeMs = 0;
            long minimumTimeMs = long.MaxValue;
            long maximumTimeMs = long.MinValue;
            List<int> listexecutionTimeMs = new List<int>();

            try
            {
                var connKey = $"{storedProcedureExecuteRequest.ServerName}_{storedProcedureExecuteRequest.DbName}";
                var connectedDb = ConnectionStore.Get(connKey);

                if (connectedDb == null)
                    throw new Exception($"No dynamic connection found for database: {storedProcedureExecuteRequest.DbName}");

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = connectedDb.ServerName,
                    InitialCatalog = storedProcedureExecuteRequest.DbName,
                    IntegratedSecurity = connectedDb.Authentication == "Windows",
                };

                if (connectedDb.Authentication == "SQL")
                {
                    builder.UserID = connectedDb.Username;
                    builder.Password = connectedDb.Password;
                }

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                long totalElapsed = 0;

                for (int i = 0; i < storedProcedureExecuteRequest.ExecutionCount; i++)
                {
                    using var command = new SqlCommand(storedProcedureExecuteRequest.ObjectName, connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    // Add parameters
                    foreach (var param in storedProcedureExecuteRequest.Parameters)
                    {
                        var cleanKey = param.Key.Split(' ')[0];

                        object cleanValue;
                        if (param.Value is JsonElement jsonElement)
                        {
                            switch (jsonElement.ValueKind)
                            {
                                case JsonValueKind.Number:
                                    if (jsonElement.TryGetInt64(out long longVal))
                                        cleanValue = longVal;
                                    else
                                        cleanValue = jsonElement.GetDecimal();
                                    break;
                                case JsonValueKind.String:
                                    cleanValue = jsonElement.GetString();
                                    break;
                                case JsonValueKind.True:
                                case JsonValueKind.False:
                                    cleanValue = jsonElement.GetBoolean();
                                    break;
                                case JsonValueKind.Null:
                                    cleanValue = DBNull.Value;
                                    break;
                                default:
                                    cleanValue = jsonElement.ToString();
                                    break;
                            }
                        }
                        else
                        {
                            cleanValue = param.Value ?? DBNull.Value;
                        }

                        command.Parameters.AddWithValue(cleanKey, cleanValue);
                    }

                    var startTime = DateTime.Now;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (i == 0) // Only load data on first run
                        {
                            // Initialize columns dynamically
                            for (int colIndex = 0; colIndex < reader.FieldCount; colIndex++)
                            {
                                var type = reader.GetFieldType(colIndex) ?? typeof(object);
                                // Ensure unique column names by appending index if needed
                                string colName = reader.GetName(colIndex);
                                if (string.IsNullOrWhiteSpace(colName)) colName = $"Column{colIndex + 1}";
                                if (pagedDt.Columns.Contains(colName)) colName += $"_{colIndex}";
                                
                                pagedDt.Columns.Add(colName, type);
                            }

                            int matchCount = 0;
                            int skip = (storedProcedureExecuteRequest.Page - 1) * storedProcedureExecuteRequest.PageSize;
                            int pageSize = storedProcedureExecuteRequest.PageSize;
                            string keyword = storedProcedureExecuteRequest.SearchKeyword?.Trim().ToLower();
                            bool hasKeyword = !string.IsNullOrWhiteSpace(keyword);

                            while (await reader.ReadAsync())
                            {
                                bool isMatch = true;
                                if (hasKeyword)
                                {
                                    isMatch = false;
                                    for (int colIndex = 0; colIndex < reader.FieldCount; colIndex++)
                                    {
                                        var val = reader.GetValue(colIndex);
                                        if (val != null && val != DBNull.Value && val.ToString().ToLower().Contains(keyword))
                                        {
                                            isMatch = true;
                                            break;
                                        }
                                    }
                                }

                                if (isMatch)
                                {
                                    if (matchCount >= skip && matchCount < skip + pageSize)
                                    {
                                        DataRow row = pagedDt.NewRow();
                                        for (int colIndex = 0; colIndex < reader.FieldCount; colIndex++)
                                        {
                                            row[colIndex] = reader.GetValue(colIndex);
                                        }
                                        pagedDt.Rows.Add(row);
                                    }
                                    matchCount++;
                                }
                            }
                            totalCount = matchCount;
                        }
                        else
                        {
                            while (await reader.ReadAsync()) { } // just iterate to simulate load
                        }
                    }

                    var endTime = DateTime.Now;
                    long elapsed = (long)(endTime - startTime).TotalMilliseconds;

                    listexecutionTimeMs.Add((int)elapsed);

                    totalElapsed += elapsed;
                    if (elapsed < minimumTimeMs) minimumTimeMs = elapsed;
                    if (elapsed > maximumTimeMs) maximumTimeMs = elapsed;
                }

                // Average execution time
                executionTimeMs = totalElapsed / storedProcedureExecuteRequest.ExecutionCount;

                // Extract column names and data types (using pagedDt instead of dt)
                foreach (DataColumn col in pagedDt.Columns)
                {
                    var typeName = col.DataType.Name;
                    if (col.DataType == typeof(string))
                        typeName = $"nvarchar({(col.MaxLength == int.MaxValue ? "MAX" : col.MaxLength)})";
                    else if (col.DataType == typeof(int))
                        typeName = "int";
                    else if (col.DataType == typeof(long))
                        typeName = "bigint";
                    else if (col.DataType == typeof(DateTime))
                        typeName = "datetime";
                    else if (col.DataType == typeof(decimal))
                        typeName = "decimal";
                    else if (col.DataType == typeof(bool))
                        typeName = "bit";

                    columnTypes[col.ColumnName] = typeName;
                }

                // Safety if no executions
                if (minimumTimeMs == long.MaxValue) minimumTimeMs = 0;
                if (maximumTimeMs == long.MinValue) maximumTimeMs = 0;

                return (pagedDt, totalCount, columnTypes, executionTimeMs, minimumTimeMs, maximumTimeMs, listexecutionTimeMs);
            }
            catch
            {
                throw;
            }
        }


        public async Task<string> GenerateStoredProcedureScriptAsync(StoredProcedureScriptRequest storedProcedureScriptRequest)
        {
            var connKey = $"{storedProcedureScriptRequest.ServerName}_{storedProcedureScriptRequest.DbName}";
            var connectedDb = ConnectionStore.Get(connKey);

            if (connectedDb == null)
                throw new Exception($"No dynamic connection found for database: {storedProcedureScriptRequest.DbName}");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = connectedDb.ServerName,
                InitialCatalog = storedProcedureScriptRequest.DbName,
                IntegratedSecurity = connectedDb.Authentication == "Windows",
            };

            if (connectedDb.Authentication == "SQL")
            {
                builder.UserID = connectedDb.Username;
                builder.Password = connectedDb.Password;
            }

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"USE [{storedProcedureScriptRequest.DbName}];");
            sb.AppendLine("GO");
            sb.AppendLine();

            // Get definition of stored procedure
            string? procDefinition = null;
            const string procQuery = @"SELECT OBJECT_DEFINITION(OBJECT_ID(@ProcName))";
            using (var cmd = new SqlCommand(procQuery, conn))
            {
                cmd.Parameters.AddWithValue("@ProcName", storedProcedureScriptRequest.ObjectName);
                procDefinition = (string?)await cmd.ExecuteScalarAsync();
            }

            if (string.IsNullOrWhiteSpace(procDefinition))
                return $"-- Stored procedure [{storedProcedureScriptRequest.ObjectName}] not found.";

            switch (storedProcedureScriptRequest.ScriptType)
            {
                case "create-script":
                    sb.AppendLine(procDefinition);
                    sb.AppendLine("GO");
                    break;

                case "drop-script":
                    sb.AppendLine($"DROP PROCEDURE IF EXISTS [dbo].[{storedProcedureScriptRequest.ObjectName}];");
                    sb.AppendLine("GO");
                    break;

                case "create-drop-script":
                    sb.AppendLine($"DROP PROCEDURE IF EXISTS [dbo].[{storedProcedureScriptRequest.ObjectName}];");
                    sb.AppendLine("GO");
                    sb.AppendLine(procDefinition);
                    sb.AppendLine("GO");
                    break;

                case "alter-script":
                    // Ensure definition starts with ALTER instead of CREATE
                    string alterDefinition = procDefinition.Replace("CREATE PROCEDURE", "ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase);
                    sb.AppendLine(alterDefinition);
                    sb.AppendLine("GO");
                    break;

                default:
                    sb.AppendLine("-- Unsupported script format for stored procedure.");
                    break;
            }

            return sb.ToString();
        }


    }
}
