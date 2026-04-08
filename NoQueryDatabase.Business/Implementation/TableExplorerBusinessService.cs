using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using NoQueryDatabase.Business.Contract;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.TableExplorerModel;
using System.Data;
using System.Data.SqlClient;
using System.Xml.Linq;

namespace NoQueryDatabase.Business.Implementation
{
    public class TableExplorerBusinessService : ITableExplorerBusinessService
    {
        private readonly ITableExplorerDataService _tableExplorerDataService;
        private readonly ILogger<TableExplorerBusinessService> _logger;

        public TableExplorerBusinessService(ITableExplorerDataService tableExplorerDataService,
            ILogger<TableExplorerBusinessService> logger)
        {
            _tableExplorerDataService = tableExplorerDataService;
            _logger = logger;
        }
        public async Task<List<string>> GetAllDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString)
        {
            try
            {
                return await _tableExplorerDataService.GetAllDatabaseNamesAsync(serverName, authentication, login, password, connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GetAllDatabaseNamesAsync");
                throw;
            }
        }
        public async Task<List<string>> GetAllTablesByDatabaseNamesAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName)
        {
            try
            {
                return await _tableExplorerDataService.GetAllTablesByDatabaseNamesAsync(serverName, authentication, login, password, connectionString, databaseName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GetAllTablesByDatabaseNamesAsync");
                throw;
            }
        }
        public async Task<List<TableColumnInfo>> GetTableColumnsByDatabaseAsync(string serverName, string authentication, string login, string password, string connectionString, string databaseName, string tableName)
        {
            try
            {
                return await _tableExplorerDataService.GetTableColumnsByDatabaseAsync(serverName, authentication, login, password, connectionString, databaseName, tableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GetTableColumnsByDatabaseAsync");
                throw;
            }
        }
        public async Task<(bool Success, List<string> Messages)> ExecuteAlterTableAsync(
    string serverName,
    string authentication,
    string login,
    string password,
    string connectionString,
    string databaseName,
    string sqlQuery)
        {
            try
            {
                return await _tableExplorerDataService.ExecuteAlterTableAsync(serverName, authentication, login, password, connectionString, databaseName, sqlQuery);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.ExecuteAlterTableAsync");
                throw;
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
            try
            {
                return await _tableExplorerDataService.GenerateAlterSqlAsync(serverName, authentication, login, password, connectionString, databaseName, tableName, columns);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GenerateAlterSqlAsync");
                throw;
            }
        }
        public async Task<(DataTable data, int filteredCount, int totalCount, Dictionary<string, string> columnTypes)> GetTableDataAsync(
            string serverName,
            string dbName,
            string tableName,
            int page,
            int pageSize,
            string filterColumn = null,
            string filterValue = null,
            string sortOrder = "ASC",
            string filterOperator = "LIKE"
        )
        {
            try
            {
                return await _tableExplorerDataService.GetTableDataAsync(serverName,dbName, tableName, page, pageSize, filterColumn, filterValue, sortOrder,filterOperator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GetTableDataAsync");
                throw;
            }
        }
        public async Task<bool> UpdateTableRowAsync(TableRowUpdateRequest updateTableRequest)
        {
            
            try
            {
                return await _tableExplorerDataService.UpdateTableRowAsync(updateTableRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.UpdateTableRowAsync");
                throw;
            }
        }
        public async Task<bool> DeleteTableRowAsync(TableRowDeleteRequest tableRowDeleteRequest)
        {
            try
            {
                return await _tableExplorerDataService.DeleteTableRowAsync(tableRowDeleteRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.DeleteTableRowAsync");
                throw;
            }
        }
        public async Task<bool> InsertTableRowAsync(TableRowInsertRequest tableRowInsertRequest)
        {
            try
            {
                return await _tableExplorerDataService.InsertTableRowAsync(tableRowInsertRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.InsertTableRowAsync");
                throw;
            }
        }
        public async Task<string> TruncateTableAsync(TableTruncateRequest tableTruncateRequest)
        {
            try
            {
                return await _tableExplorerDataService.TruncateTableAsync(tableTruncateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.TruncateTableAsync");
                throw;
            }
        }
        public async Task<string> DropTableAsync(TableDropRequest tableTruncateRequest)
        {
            try
            {
                return await _tableExplorerDataService.DropTableAsync(tableTruncateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.DropTableAsync");
                throw;
            }
        }
        public async Task<bool> CreateTableAsync(TableCreateRequest tableCreateRequest)
        {
            try
            {
                return await _tableExplorerDataService.CreateTableAsync(tableCreateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.CreateTableAsync");
                throw;
            }
        }
        public async Task<(DataTable,int, int)> SearchTableDataAsync(
            string serverName,
    string dbName,
    string tableName,
    string keyword,
    int page = 1,
    int pageSize = 10)
        {
            try
            {
                return await _tableExplorerDataService.SearchTableDataAsync(serverName,dbName, tableName, keyword, page, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.SearchTableDataAsync");
                throw;
            }
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
            try
            {
                return await _tableExplorerDataService.ExportTableDataAsync(dbName, tableName, keyword, filterColumn, filterOperator, filterValue, visibleColumns, sortOrder,chunkSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.ExportTableDataAsync");
                throw;
            }
        }
        public async Task<string> GenerateTableScriptAsync(string dbName, string tableName, string format)
        {
            try
            {
                return await _tableExplorerDataService.GenerateTableScriptAsync(dbName, tableName, format);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TableExplorerBusinessService.GenerateTableScriptAsync");
                throw;
            }
        }
    }
}
