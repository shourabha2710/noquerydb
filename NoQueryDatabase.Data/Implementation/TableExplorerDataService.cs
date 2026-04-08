using System.Data;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.TableExplorerModel;

namespace NoQueryDatabase.Data.Implementation
{
    public class TableExplorerDataService : ITableExplorerDataService
    {
        private readonly ISchemaDiscoveryService _schemaDiscoveryService;
        private readonly IDataOperationService _dataOperationService;

        public TableExplorerDataService(ISchemaDiscoveryService schemaDiscoveryService, IDataOperationService dataOperationService)
        {
            _schemaDiscoveryService = schemaDiscoveryService;
            _dataOperationService = dataOperationService;
        }

        public async Task<List<string>> GetAllDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString)
            => await _schemaDiscoveryService.GetAllDatabaseNamesAsync(serverName, authentication, login, password, connectionString);

        public async Task<List<string>> GetAllTablesByDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName)
            => await _schemaDiscoveryService.GetAllTablesByDatabaseNamesAsync(serverName, authentication, login, password, connectionString, databaseName);

        public async Task<List<TableColumnInfo>> GetTableColumnsByDatabaseAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName, string tableName)
            => await _schemaDiscoveryService.GetTableColumnsByDatabaseAsync(serverName, authentication, login, password, connectionString, databaseName, tableName);

        public async Task<string> GenerateTableScriptAsync(string dbName, string tableName, string format)
            => await _schemaDiscoveryService.GenerateTableScriptAsync(dbName, tableName, format);

        public async Task<(DataTable, int, int, Dictionary<string, string>)> GetTableDataAsync(string serverName, string dbName, string tableName, int page, int pageSize, string filterColumn = null, string filterValue = null, string sortOrder = "DESC", string filterOperator = "LIKE")
            => await _dataOperationService.GetTableDataAsync(serverName, dbName, tableName, page, pageSize, filterColumn, filterValue, sortOrder, filterOperator);

        public async Task<bool> InsertTableRowAsync(TableRowInsertRequest tableRowInsertRequest)
            => await _dataOperationService.InsertTableRowAsync(tableRowInsertRequest);

        public async Task<bool> UpdateTableRowAsync(TableRowUpdateRequest updateTableRequest)
            => await _dataOperationService.UpdateTableRowAsync(updateTableRequest);

        public async Task<bool> DeleteTableRowAsync(TableRowDeleteRequest tableRowDeleteRequest)
            => await _dataOperationService.DeleteTableRowAsync(tableRowDeleteRequest);

        public async Task<string> TruncateTableAsync(TableTruncateRequest tableTruncateRequest)
            => await _dataOperationService.TruncateTableAsync(tableTruncateRequest);

        public async Task<string> DropTableAsync(TableDropRequest tableDropRequest)
            => await _dataOperationService.DropTableAsync(tableDropRequest);

        public async Task<bool> CreateTableAsync(TableCreateRequest tableCreateRequest)
            => await _dataOperationService.CreateTableAsync(tableCreateRequest);

        public async Task<(DataTable, int, int)> SearchTableDataAsync(string serverName, string dbName, string tableName, string keyword, int page = 1, int pageSize = 10)
            => await _dataOperationService.SearchTableDataAsync(serverName, dbName, tableName, keyword, page, pageSize);

        public async Task<List<DataTable>> ExportTableDataAsync(string dbName, string tableName, string keyword = "", string filterColumn = "", string filterOperator = "LIKE", string filterValue = "", List<string> visibleColumns = null, string sortOrder = "", int chunkSize = 50)
            => await _dataOperationService.ExportTableDataAsync(dbName, tableName, keyword, filterColumn, filterOperator, filterValue, visibleColumns, sortOrder, chunkSize);

        public async Task<(bool Success, string SqlScript, string Message)> GenerateAlterSqlAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName, string tableName, List<ColumnAlterModel> columns)
            => await _dataOperationService.GenerateAlterSqlAsync(serverName, authentication, login, password, connectionString, databaseName, tableName, columns);

        public async Task<(bool Success, List<string> Messages)> ExecuteAlterTableAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName, string sqlQuery)
            => await _dataOperationService.ExecuteAlterTableAsync(serverName, authentication, login, password, connectionString, databaseName, sqlQuery);
    }
}
