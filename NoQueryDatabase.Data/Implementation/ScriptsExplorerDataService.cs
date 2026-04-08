using Dapper;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using System.Data.SqlClient;

namespace NoQueryDatabase.Data.Implementation
{
    public class ScriptsExplorerDataService: IScriptsExplorerDataService
    {
        public async Task<List<DatabaseMetadata>> GetTables(string serverName, string authentication, string login, string password, string connectionString)
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

                builder.InitialCatalog = "master"; // start with master to get DB list
                connStr = builder.ToString();
            }


            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var databases = (await conn.QueryAsync<string>("SELECT name FROM sys.databases WHERE database_id > 4")).ToList();

            var dbList = new List<DatabaseMetadata>();

            foreach (var dbName in databases)
            {
                var dbMeta = new DatabaseMetadata { Name = dbName };
                var builder = new SqlConnectionStringBuilder(connStr) { InitialCatalog = dbName };
                using var dbConn = new SqlConnection(builder.ToString());
                await dbConn.OpenAsync();

                dbMeta.ServerName = serverName;

                dbMeta.Tables = (await dbConn.QueryAsync<string>(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")).ToList();
                dbList.Add(dbMeta);
            }

            return dbList;
        }
        public async Task<List<DatabaseMetadata>> GetStoredProcedures(string serverName, string authentication, string login, string password, string connectionString)
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

                builder.InitialCatalog = "master"; // start with master to get DB list
                connStr = builder.ToString();
            }


            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var databases = (await conn.QueryAsync<string>("SELECT name FROM sys.databases WHERE database_id > 4")).ToList();

            var dbList = new List<DatabaseMetadata>();

            foreach (var dbName in databases)
            {
                var dbMeta = new DatabaseMetadata { Name = dbName };
                var builder = new SqlConnectionStringBuilder(connStr) { InitialCatalog = dbName };
                using var dbConn = new SqlConnection(builder.ToString());
                await dbConn.OpenAsync();

                dbMeta.ServerName = serverName;

                dbMeta.StoredProcedures = (await dbConn.QueryAsync<string>(
                         "SELECT name FROM sys.procedures")).ToList();
                dbList.Add(dbMeta);
            }

            return dbList;
        }
        public async Task<List<DatabaseMetadata>> GetFunctions(string serverName, string authentication, string login, string password, string connectionString)
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

                builder.InitialCatalog = "master"; // start with master to get DB list
                connStr = builder.ToString();
            }


            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var databases = (await conn.QueryAsync<string>("SELECT name FROM sys.databases WHERE database_id > 4")).ToList();

            var dbList = new List<DatabaseMetadata>();

            foreach (var dbName in databases)
            {
                var dbMeta = new DatabaseMetadata { Name = dbName };
                var builder = new SqlConnectionStringBuilder(connStr) { InitialCatalog = dbName };
                using var dbConn = new SqlConnection(builder.ToString());
                await dbConn.OpenAsync();

                dbMeta.ServerName = serverName;

                dbMeta.Functions = (await dbConn.QueryAsync<string>(
                         "SELECT name FROM sys.objects WHERE type IN ('FN','IF','TF')")).ToList();
                dbList.Add(dbMeta);
            }

            return dbList;
        }
        public async Task<List<DatabaseMetadata>> GetViews(string serverName, string authentication, string login, string password, string connectionString)
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

                builder.InitialCatalog = "master"; // start with master to get DB list
                connStr = builder.ToString();
            }


            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var databases = (await conn.QueryAsync<string>("SELECT name FROM sys.databases WHERE database_id > 4")).ToList();

            var dbList = new List<DatabaseMetadata>();

            foreach (var dbName in databases)
            {
                var dbMeta = new DatabaseMetadata { Name = dbName };
                var builder = new SqlConnectionStringBuilder(connStr) { InitialCatalog = dbName };
                using var dbConn = new SqlConnection(builder.ToString());
                await dbConn.OpenAsync();

                dbMeta.ServerName = serverName;

                dbMeta.Views = (await dbConn.QueryAsync<string>(
                        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS")).ToList();
                dbList.Add(dbMeta);
            }

            return dbList;
        }
    }
}
