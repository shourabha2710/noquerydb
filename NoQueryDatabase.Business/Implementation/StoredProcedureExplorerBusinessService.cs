using Microsoft.Extensions.Logging;
using NoQueryDatabase.Business.Contract;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.StoredProcedureExplorerModel;
using System.Data;
using System.Xml.Linq;

namespace NoQueryDatabase.Business.Implementation
{
    public class StoredProcedureExplorerBusinessService : IStoredProcedureExplorerBusinessService
    {
        private readonly IStoredProcedureExplorerDataService _storedProcedureExplorerDataService;
        private readonly ILogger<StoredProcedureExplorerBusinessService> _logger;
        public StoredProcedureExplorerBusinessService(IStoredProcedureExplorerDataService storedProcedureExplorerDataService,
           ILogger<StoredProcedureExplorerBusinessService> logger)
        {
            _storedProcedureExplorerDataService = storedProcedureExplorerDataService;
            _logger = logger;
        }
        public async Task<List<string>> GetStoredProcedureParameters(string StoredProcedureName, string ServerName, string DbName)
        {
            try
            {
                return await _storedProcedureExplorerDataService.GetStoredProcedureParameters(StoredProcedureName, ServerName, DbName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in StoredProcedureExplorerBusinessService.GetStoredProcedureParameters");
                throw;
            }
        
        }
        public async Task<(DataTable, int,Dictionary<string, string>,long, long, long, List<int>)> ExecuteStoredProcedure(StoredProcedureExecuteRequest storedProcedureExecuteRequest)
        {
            try
            {
                return await _storedProcedureExplorerDataService.ExecuteStoredProcedure(storedProcedureExecuteRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in StoredProcedureExplorerBusinessService.ExecuteStoredProcedure");
                throw;
            }

        }
        public async Task<(DataTable, int, Dictionary<string, string>, long, long, long, List<int>)> ExecuteSearchStoredProcedure(StoredProcedureExecuteRequest storedProcedureExecuteRequest)
        {
            try
            {
                return await _storedProcedureExplorerDataService.ExecuteSearchStoredProcedure(storedProcedureExecuteRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in StoredProcedureExplorerBusinessService.ExecuteSearchStoredProcedure");
                throw;
            }

        }
        public async Task<string> GenerateStoredProcedureScriptAsync(StoredProcedureScriptRequest storedProcedureScriptRequest)
        {
            try
            {
                return await _storedProcedureExplorerDataService.GenerateStoredProcedureScriptAsync(storedProcedureScriptRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in StoredProcedureExplorerBusinessService.GenerateStoredProcedureScriptAsync");
                throw;
            }

        }
    }
}
