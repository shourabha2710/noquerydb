using System.Data;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.TableExplorerModel;

namespace NoQueryDatabase.Data.Contract
{
    public interface IDataOperationService
    {
        Task<(DataTable data, int filteredCount, int totalCount, Dictionary<string, string> columnTypes)> GetTableDataAsync(
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

        Task<bool> InsertTableRowAsync(TableRowInsertRequest tableRowInsertRequest);
        Task<bool> UpdateTableRowAsync(TableRowUpdateRequest updateTableRequest);
        Task<bool> DeleteTableRowAsync(TableRowDeleteRequest tableRowDeleteRequest);
        Task<string> TruncateTableAsync(TableTruncateRequest tableTruncateRequest);
        Task<string> DropTableAsync(TableDropRequest tableDropRequest);
        Task<bool> CreateTableAsync(TableCreateRequest tableCreateRequest);

        Task<(DataTable data, int filteredCount, int totalCount)> SearchTableDataAsync(
            string serverName,
            string dbName,
            string tableName,
            string keyword,
            int page = 1,
            int pageSize = 10);

        Task<List<DataTable>> ExportTableDataAsync(
            string dbName,
            string tableName,
            string keyword = "",
            string filterColumn = "",
            string filterOperator = "LIKE",
            string filterValue = "",
            List<string> visibleColumns = null,
            string sortOrder = "",
            int chunkSize = 50);

        Task<(bool Success, string SqlScript, string Message)> GenerateAlterSqlAsync(
            string serverName,
            string authentication,
            string login,
            string password,
            string connectionString,
            string databaseName,
            string tableName,
            List<ColumnAlterModel> columns);

        Task<(bool Success, List<string> Messages)> ExecuteAlterTableAsync(
            string serverName,
            string authentication,
            string login,
            string password,
            string connectionString,
            string databaseName,
            string sqlQuery);
    }
}
