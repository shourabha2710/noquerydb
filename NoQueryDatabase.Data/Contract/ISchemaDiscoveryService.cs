using NoQueryDatabase.Model.TableExplorerModel;

namespace NoQueryDatabase.Data.Contract
{
    public interface ISchemaDiscoveryService
    {
        Task<List<string>> GetAllDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString);
        Task<List<string>> GetAllTablesByDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName);
        Task<List<TableColumnInfo>> GetTableColumnsByDatabaseAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName, string tableName);
        Task<string> GenerateTableScriptAsync(string dbName, string tableName, string format);
        Task<bool> TableExistsAsync(string connectionString, string sqlQuery);
    }
}
