using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Data.Contract
{
    public interface IViewsExplorerDataService
    {
        Task<(DataTable, int, int, Dictionary<string, string>)> GetViewDataAsync(
    string serverName,
    string dbName,
    string viewName,
    int page,
    int pageSize,
    string filterColumn = null,
    string filterValue = null,
    string sortOrder = "DESC",
    string filterOperator = "LIKE");
        public Task<(DataTable, int, int)> SearchViewsDataAsync(
            string serverName,
    string dbName,
    string tableName,
    string keyword,
    int page = 1,
    int pageSize = 10);
        public Task<string> GenerateViewsScriptAsync(string dbName, string tableName, string format);
    }

}
