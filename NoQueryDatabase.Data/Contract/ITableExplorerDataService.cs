using NoQueryDatabase.Data.Implementation;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.TableExplorerModel;
using System.Data;

namespace NoQueryDatabase.Data.Contract
{
    public interface ITableExplorerDataService
    {
        public Task<List<string>> GetAllDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString);
        public Task<List<string>> GetAllTablesByDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString,string databaseName);
        public Task<List<TableColumnInfo>> GetTableColumnsByDatabaseAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName,string tableName);
        public Task<(bool Success, List<string> Messages)> ExecuteAlterTableAsync(
    string serverName,
    string authentication,
    string login,
    string password,
    string connectionString,
    string databaseName,
    string sqlQuery);
        public Task<(bool Success, string SqlScript, string Message)> GenerateAlterSqlAsync(
    string serverName,
    string authentication,
    string login,
    string password,
    string connectionString,
    string databaseName,
    string tableName,
    List<ColumnAlterModel> columns);
        public Task<(DataTable data, int filteredCount, int totalCount, Dictionary<string, string> columnTypes)> GetTableDataAsync(
            string serverName,
            string dbName,
            string tableName,
            int page,
            int pageSize,
            string filterColumn = null,
            string filterValue = null,
            string sortOrder = "ASC",
            string filterOperator = "LIKE"
        );
        public Task<bool> UpdateTableRowAsync(TableRowUpdateRequest updateTableRequest);
        public Task<bool> DeleteTableRowAsync(TableRowDeleteRequest tableRowDeleteRequest);
        public Task<bool> InsertTableRowAsync(TableRowInsertRequest tableRowInsertRequest);
        public Task<string> TruncateTableAsync(TableTruncateRequest tableTruncateRequest);
        public Task<string> DropTableAsync(TableDropRequest tableTruncateRequest);
        public Task<bool> CreateTableAsync(TableCreateRequest tableCreateRequest);

        public Task<(DataTable,int, int)> SearchTableDataAsync(
            string serverName,
    string dbName,
    string tableName,
    string keyword,
    int page = 1,
    int pageSize = 10);
        public Task<List<DataTable>> ExportTableDataAsync(
    string dbName,
    string tableName,
    string keyword = "",
    string filterColumn = "",
    string filterOperator = "LIKE",
    string filterValue = "",
    List<string> visibleColumns = null,
     string sortOrder = "",
    int chunkSize = 50);
        public Task<string> GenerateTableScriptAsync(string dbName, string tableName, string format);
    }


}
