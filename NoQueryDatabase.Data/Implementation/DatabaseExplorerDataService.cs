using Microsoft.Extensions.Configuration;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using System.Data.SqlClient;
using Dapper;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Utility;
using System.Xml.Linq;

namespace NoQueryDatabase.Data.Implementation
{
    public class DatabaseExplorerDataService : IDatabaseExplorerDataService
    {
        private readonly string _masterConnectionString;
        public DatabaseExplorerDataService(IConfiguration config)
        {
            _masterConnectionString = null;
        }
        public async Task<List<DatabaseMetadata>> GetAllDatabaseMetadataAsync()
        {
            var result = new List<DatabaseMetadata>();

            using var conn = new SqlConnection(_masterConnectionString);
            await conn.OpenAsync();

            var databases = (await conn.QueryAsync<string>(
                "SELECT name FROM sys.databases WHERE database_id > 4")).ToList();

            foreach (var dbName in databases)
            {
                var dbMeta = new DatabaseMetadata { Name = dbName };
                var builder = new SqlConnectionStringBuilder(_masterConnectionString)
                {
                    InitialCatalog = dbName
                };

                using var dbConn = new SqlConnection(builder.ToString());
                await dbConn.OpenAsync();
                dbMeta.ServerName = "172.21.150.130";
                dbMeta.Tables = (await dbConn.QueryAsync<string>(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")).ToList();

                dbMeta.Views = (await dbConn.QueryAsync<string>(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS")).ToList();

                dbMeta.StoredProcedures = (await dbConn.QueryAsync<string>(
                    "SELECT name FROM sys.procedures")).ToList();

                dbMeta.Functions = (await dbConn.QueryAsync<string>(
                    "SELECT name FROM sys.objects WHERE type IN ('FN','IF','TF')")).ToList();

                result.Add(dbMeta);
            }

            return result;
        }
        public async Task<List<DatabaseMetadata>> ConnectNewDatabase(DynamicConnectionRequest dynamicConnectionRequest)
        {
            try
            {
                string connStr;
                if (!string.IsNullOrWhiteSpace(dynamicConnectionRequest.ConnectionString))
                {
                    connStr = dynamicConnectionRequest.ConnectionString;
                }
                else
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = dynamicConnectionRequest.ServerName,
                        IntegratedSecurity = dynamicConnectionRequest.Authentication == "Windows"
                    };

                    if (dynamicConnectionRequest.Authentication == "SQL")
                    {
                        builder.UserID = dynamicConnectionRequest.Login;
                        builder.Password = dynamicConnectionRequest.Password;
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

                    dbMeta.ServerName= dynamicConnectionRequest.ServerName;

                    //dbMeta.Tables = (await dbConn.QueryAsync<string>(
                    //    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")).ToList();

                    //dbMeta.Views = (await dbConn.QueryAsync<string>(
                    //    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS")).ToList();

                    //dbMeta.StoredProcedures = (await dbConn.QueryAsync<string>(
                    //    "SELECT name FROM sys.procedures")).ToList();

                    //dbMeta.Functions = (await dbConn.QueryAsync<string>(
                    //    "SELECT name FROM sys.objects WHERE type IN ('FN','IF','TF')")).ToList();

                    dbMeta.Tables = (await dbConn.QueryAsync<string>(
    @"SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS TABLE_NAME
      FROM INFORMATION_SCHEMA.TABLES 
      WHERE TABLE_TYPE = 'BASE TABLE'")).ToList();

                    // Views with schema
                    dbMeta.Views = (await dbConn.QueryAsync<string>(
                        @"SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS TABLE_NAME
      FROM INFORMATION_SCHEMA.VIEWS")).ToList();

                    // Stored Procedures with schema
                    dbMeta.StoredProcedures = (await dbConn.QueryAsync<string>(
                        @"SELECT SCHEMA_NAME(schema_id) + '.' + name AS name
      FROM sys.procedures")).ToList();

                    // Functions with schema
                    dbMeta.Functions = (await dbConn.QueryAsync<string>(
                        @"SELECT SCHEMA_NAME(schema_id) + '.' + name AS name
      FROM sys.objects 
      WHERE type IN ('FN','IF','TF')")).ToList();
                    ConnectionStore.AddOrUpdate($"{dynamicConnectionRequest.ServerName}_{dbName}", new ConnectedDatabase
                    {
                        ServerName = dynamicConnectionRequest.ServerName,
                        DatabaseName = dbName,
                        Authentication = dynamicConnectionRequest.Authentication,
                        Username = dynamicConnectionRequest.Login,
                        Password = dynamicConnectionRequest.Password
                    });
                    dbList.Add(dbMeta);
                }

                return dbList;
            }
            catch (Exception ex)
            {
                 throw ex;
            }
        }
    }
}
