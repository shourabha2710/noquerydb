using Microsoft.Extensions.Logging;
using NoQueryDatabase.Business.Contract;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Data.Implementation;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Business.Implementation
{
    public class ViewsExplorerBusinessService: IViewsExplorerBusinessService
    {
        private readonly IViewsExplorerDataService _viewsExplorerDataService;
        private readonly ILogger<ViewsExplorerBusinessService> _logger;

        public ViewsExplorerBusinessService(IViewsExplorerDataService viewsExplorerDataService,
            ILogger<ViewsExplorerBusinessService> logger)
        {
            _viewsExplorerDataService = viewsExplorerDataService;
            _logger = logger;
        }
        public async Task<(DataTable, int, int, Dictionary<string, string>)> GetViewDataAsync(
    string serverName,
    string dbName,
    string viewName,
    int page,
    int pageSize,
    string filterColumn = null,
    string filterValue = null,
    string sortOrder = "DESC",
    string filterOperator = "LIKE")
        {
            try
            {
                return await _viewsExplorerDataService.GetViewDataAsync(serverName, dbName, viewName, page, pageSize, filterColumn, filterValue, sortOrder, filterOperator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ViewsExplorerBusinessService.GetViewDataAsync");
                throw;
            }
        }
        public async Task<(DataTable, int, int)> SearchViewsDataAsync(
            string serverName,
    string dbName,
    string tableName,
    string keyword,
    int page = 1,
    int pageSize = 10)
        {
            try
            {
                return await _viewsExplorerDataService.SearchViewsDataAsync(serverName, dbName, tableName, keyword, page, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ViewsExplorerBusinessService.SearchViewsDataAsync");
                throw;
            }
        }
        public async Task<string> GenerateViewsScriptAsync(string dbName, string tableName, string format)
        {
            try
            {
                return await _viewsExplorerDataService.GenerateViewsScriptAsync(dbName, tableName, format);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ViewsExplorerBusinessService.GenerateViewsScriptAsync");
                throw;
            }
        }
    }
}
