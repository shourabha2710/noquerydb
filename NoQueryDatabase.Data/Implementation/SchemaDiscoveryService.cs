using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.TableExplorerModel;
using Dapper;
using System.Text.RegularExpressions;
using System.Text;

namespace NoQueryDatabase.Data.Implementation
{
    public class SchemaDiscoveryService : ISchemaDiscoveryService
    {
        private readonly string _masterConnectionString;
        private readonly IMetadataProvider _metadataProvider;

        public SchemaDiscoveryService(IConfiguration config, IMetadataProvider metadataProvider)
        {
            _masterConnectionString = config.GetConnectionString("MasterDB");
            _metadataProvider = metadataProvider;
        }

        public async Task<List<string>> GetAllDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString)
        {
            string connStr;
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                connStr = connectionString;
            }
            else
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = serverName,
                    IntegratedSecurity = authentication == "Windows"
                };

                if (authentication == "SQL")
                {
                    builder.UserID = login;
                    builder.Password = password;
                }

                builder.InitialCatalog = "master"; // connect to master
                connStr = builder.ToString();
            }

            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Skip system databases (id <= 4)
            var databases = (await conn.QueryAsync<string>(
                "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name"
            )).ToList();

            return databases;
        }

        public async Task<List<string>> GetAllTablesByDatabaseNamesAsync(
            string serverName,
            string authentication,
            string login,
            string password,
            string connectionString,
            string databaseName)
        {
            return await _metadataProvider.GetMetadataAsync(serverName, databaseName, "Tables", async () =>
            {
                string connStr;

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    // Use provided connection string
                    var csBuilder = new SqlConnectionStringBuilder(connectionString)
                    {
                        InitialCatalog = databaseName // Ensure it connects to the specific DB
                    };
                    connStr = csBuilder.ToString();
                }
                else
                {
                    // Build connection string manually
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = serverName,
                        InitialCatalog = databaseName,
                        IntegratedSecurity = authentication.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                    };

                    if (authentication.Equals("SQL", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.UserID = login;
                        builder.Password = password;
                    }

                    connStr = builder.ToString();
                }

                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                const string query = @"
SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;";

                return (await conn.QueryAsync<string>(query)).ToList();
            });
        }

        public async Task<List<TableColumnInfo>> GetTableColumnsByDatabaseAsync(
            string serverName,
            string authentication,
            string login,
            string password,
            string connectionString,
            string databaseName,
            string tableName)
        {
            return await _metadataProvider.GetMetadataAsync(serverName, databaseName, $"TableColumns_{tableName}", async () =>
            {
                string connStr;
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    var csBuilder = new SqlConnectionStringBuilder(connectionString)
                    {
                        InitialCatalog = databaseName
                    };
                    connStr = csBuilder.ToString();
                }
                else
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = serverName,
                        InitialCatalog = databaseName,
                        IntegratedSecurity = authentication.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                    };

                    if (authentication.Equals("SQL", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.UserID = login;
                        builder.Password = password;
                    }

                    connStr = builder.ToString();
                }

                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                var cleanTableName = tableName.Replace("dbo.", "");
                const string query = @"
        SELECT 
            c.COLUMN_NAME AS ColumnName,
            c.DATA_TYPE AS DataType,
            c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
            c.IS_NULLABLE AS IsNullable,
            COLUMNPROPERTY(OBJECT_ID(c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
            ISNULL(dc.definition, '') AS DefaultValue,
            CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT ku.TABLE_NAME, ku.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        ) pk ON c.TABLE_NAME = pk.TABLE_NAME AND c.COLUMN_NAME = pk.COLUMN_NAME
        LEFT JOIN sys.default_constraints dc
            ON dc.parent_object_id = OBJECT_ID(c.TABLE_NAME)
            AND dc.parent_column_id = COLUMNPROPERTY(OBJECT_ID(c.TABLE_NAME), c.COLUMN_NAME, 'ColumnID')
        WHERE c.TABLE_NAME = @TableName
        ORDER BY c.ORDINAL_POSITION";

                var columns = (await conn.QueryAsync<TableColumnInfo>(query, new { TableName = cleanTableName })).ToList();
                return columns;
            });
        }

        public async Task<string> GenerateTableScriptAsync(string dbName, string tableName, string format)
        {
            var builder = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = dbName,
                MultipleActiveResultSets = true
            };

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            var parts = tableName.Split('.');
            var schema = parts[0];
            var table = parts[1];
            string schemaTable = tableName;

            var columns = new List<(string Name, string Type, bool IsNullable, bool IsIdentity, string? DefaultValue)>();
            var primaryKeys = new List<string>();
            var foreignKeys = new List<string>();
            var uniqueConstraints = new List<string>();
            var checkConstraints = new List<string>();
            var triggers = new List<string>();
            var indexes = new Dictionary<string, List<string>>();

            // Columns
            {
                const string columnQuery = @"
SELECT 
    c.COLUMN_NAME, 
    c.DATA_TYPE, 
    c.IS_NULLABLE, 
    c.CHARACTER_MAXIMUM_LENGTH,
    col.is_identity,
    def.definition AS DefaultValue
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN sys.columns col ON col.name = c.COLUMN_NAME
    AND col.object_id = OBJECT_ID(@SchemaTableName)
LEFT JOIN sys.default_constraints def ON def.parent_column_id = col.column_id 
    AND def.parent_object_id = col.object_id
WHERE c.TABLE_NAME = @TableName AND c.TABLE_SCHEMA = 'dbo'
ORDER BY c.ORDINAL_POSITION;";

                using var cmd = new SqlCommand(columnQuery, conn);
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@SchemaTableName", schemaTable);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string name = reader.GetString(0);
                    string type = reader.GetString(1);
                    string isNullable = reader.GetString(2);
                    object maxLen = reader.IsDBNull(3) ? null : reader.GetValue(3);
                    bool isIdentity = !reader.IsDBNull(4) && reader.GetBoolean(4);
                    string? defaultValue = reader.IsDBNull(5) ? null : reader.GetString(5);

                    string fullType = type switch
                    {
                        "varchar" or "nvarchar" or "char" => $"{type}({(maxLen?.ToString() == "-1" ? "MAX" : maxLen ?? "MAX")})",
                        _ => type
                    };

                    columns.Add((name, fullType, isNullable == "YES", isIdentity, defaultValue));
                }
            }

            // Primary Keys
            if (format != "constraints-script" && format != "triggers-script" && format != "indexes-script")
            {
                const string pkQuery = @"
SELECT COLUMN_NAME 
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
AND TABLE_NAME = @TableName AND TABLE_SCHEMA = 'dbo'";

                using var pkCmd = new SqlCommand(pkQuery, conn);
                pkCmd.Parameters.AddWithValue("@TableName", table);
                using var pkReader = await pkCmd.ExecuteReaderAsync();
                while (await pkReader.ReadAsync())
                    primaryKeys.Add(pkReader.GetString(0));
            }

            // Foreign Keys
            if (format != "constraints-script" && format != "triggers-script" && format != "indexes-script")
            {
                const string fkQuery = @"
SELECT 
    f.name AS FK_Name,
    COL_NAME(fc.parent_object_id, fc.parent_column_id) AS FK_Column,
    OBJECT_NAME(f.referenced_object_id) AS PK_Table,
    COL_NAME(fc.referenced_object_id, fc.referenced_column_id) AS PK_Column
FROM sys.foreign_keys f
JOIN sys.foreign_key_columns fc ON f.object_id = fc.constraint_object_id
WHERE f.parent_object_id = OBJECT_ID(@SchemaTableName)";

                using var fkCmd = new SqlCommand(fkQuery, conn);
                fkCmd.Parameters.AddWithValue("@SchemaTableName", schemaTable);
                using var fkReader = await fkCmd.ExecuteReaderAsync();
                while (await fkReader.ReadAsync())
                {
                    string fkCol = fkReader.GetString(1);
                    string pkTable = fkReader.GetString(2);
                    string pkCol = fkReader.GetString(3);
                    foreignKeys.Add($"FOREIGN KEY ([{fkCol}]) REFERENCES [{pkTable}]([{pkCol}])");
                }
            }

            // Constraints
            if (format == "constraints-script")
            {
                const string uqQuery = @"
SELECT tc.CONSTRAINT_NAME, kcu.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
    AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
WHERE tc.TABLE_NAME = @Table
  AND tc.TABLE_SCHEMA = @Schema
  AND tc.CONSTRAINT_TYPE = 'UNIQUE';";

                using var uqCmd = new SqlCommand(uqQuery, conn);
                uqCmd.Parameters.AddWithValue("@Table", table);
                uqCmd.Parameters.AddWithValue("@Schema", schema);
                using var uqReader = await uqCmd.ExecuteReaderAsync();
                while (await uqReader.ReadAsync())
                {
                    string col = uqReader.GetString(1);
                    uniqueConstraints.Add($"UNIQUE ([{col}])");
                }

                const string ckQuery = @"
SELECT c.name, c.definition
FROM sys.check_constraints c
JOIN sys.tables t ON c.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name = @Table
  AND s.name = @Schema;";

                using var ckCmd = new SqlCommand(ckQuery, conn);
                ckCmd.Parameters.AddWithValue("@Table", table);
                ckCmd.Parameters.AddWithValue("@Schema", schema);
                using var ckReader = await ckCmd.ExecuteReaderAsync();
                while (await ckReader.ReadAsync())
                    checkConstraints.Add(ckReader.GetString(1));
            }

            // Triggers
            if (format == "triggers-script")
            {
                const string triggerQuery = @"
SELECT name, OBJECT_DEFINITION(object_id)
FROM sys.triggers
WHERE parent_id = OBJECT_ID(@SchemaTableName)";

                using var tCmd = new SqlCommand(triggerQuery, conn);
                tCmd.Parameters.AddWithValue("@SchemaTableName", schemaTable);
                using var tReader = await tCmd.ExecuteReaderAsync();
                while (await tReader.ReadAsync())
                    triggers.Add(tReader.GetString(1));
            }

            // Indexes
            if (format == "indexes-script")
            {
                const string indexQuery = @"
SELECT i.name, c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0
AND i.object_id = OBJECT_ID(@SchemaTableName)";

                using var iCmd = new SqlCommand(indexQuery, conn);
                iCmd.Parameters.AddWithValue("@SchemaTableName", schemaTable);
                using var iReader = await iCmd.ExecuteReaderAsync();
                while (await iReader.ReadAsync())
                {
                    string idxName = iReader.GetString(0);
                    string colName = iReader.GetString(1);

                    if (!indexes.ContainsKey(idxName))
                        indexes[idxName] = new List<string>();
                    indexes[idxName].Add(colName);
                }
            }

            // Script building
            var sb = new StringBuilder();
            sb.AppendLine($"USE [{dbName}];\nGO\n");

            switch (format)
            {
                case "create-script":
                case "drop-script":
                case "create-drop-script":
                    if (format != "create-script")
                    {
                        sb.AppendLine($"DROP TABLE IF EXISTS [{dbName}].[{schema}].[{table}];\nGO\n");
                    }

                    if (format != "drop-script")
                    {
                        sb.AppendLine($"CREATE TABLE [{dbName}].[{schema}].[{table}] (");
                        for (int i = 0; i < columns.Count; i++)
                        {
                            var col = columns[i];
                            sb.Append($"    [{col.Name}] {col.Type} ");
                            if (col.IsIdentity) sb.Append("IDENTITY(1,1) ");
                            if (col.DefaultValue != null) sb.Append($"DEFAULT {col.DefaultValue} ");
                            sb.Append(col.IsNullable ? "NULL" : "NOT NULL");
                            if (i < columns.Count - 1 || primaryKeys.Count > 0 || foreignKeys.Count > 0)
                                sb.AppendLine(",");
                            else
                                sb.AppendLine();
                        }

                        if (primaryKeys.Count > 0)
                        {
                            sb.AppendLine($"    CONSTRAINT [PK_{table}] PRIMARY KEY ({string.Join(", ", primaryKeys.Select(p => $"[{p}]"))})");
                            if (foreignKeys.Count > 0)
                                sb.AppendLine(",");
                        }

                        for (int i = 0; i < foreignKeys.Count; i++)
                        {
                            sb.Append("    " + foreignKeys[i]);
                            if (i < foreignKeys.Count - 1)
                                sb.AppendLine(",");
                            else
                                sb.AppendLine();
                        }

                        sb.AppendLine(");\nGO");
                    }
                    break;

                case "keys-script":
                    if (primaryKeys.Count > 0)
                        sb.AppendLine($"ALTER TABLE [{dbName}].[{schema}].[{table}] ADD CONSTRAINT [PK_{table}] PRIMARY KEY ({string.Join(", ", primaryKeys.Select(p => $"[{p}]"))});");
                    foreach (var fk in foreignKeys)
                        sb.AppendLine($"ALTER TABLE [{dbName}].[{schema}].[{table}] ADD {fk};");
                    break;

                case "constraints-script":
                    foreach (var uc in uniqueConstraints)
                        sb.AppendLine($"ALTER TABLE [{dbName}].[{schema}].[{table}] ADD {uc};");
                    foreach (var cc in checkConstraints)
                        sb.AppendLine($"ALTER TABLE [{dbName}].[{schema}].[{table}] ADD CONSTRAINT CK_{Guid.NewGuid():N} CHECK {cc};");
                    break;

                case "triggers-script":
                    foreach (var trg in triggers)
                        sb.AppendLine(trg + "\nGO\n");
                    break;

                case "indexes-script":
                    foreach (var kv in indexes)
                        sb.AppendLine($"CREATE INDEX [{kv.Key}] ON [{dbName}].[{schema}].[{table}] ({string.Join(", ", kv.Value.Select(c => $"[{c}]"))});");
                    break;

                default:
                    sb.AppendLine("-- Unsupported script format.");
                    break;
            }

            return sb.ToString();
        }

        public async Task<bool> TableExistsAsync(string connectionString, string sqlQuery)
        {
            // Note: The interface specifies string connectionString, so I've updated the signature.
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

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
    }
}
