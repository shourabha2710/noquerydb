using Microsoft.Extensions.Logging;
using NoQueryDatabase.Business.Contract;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.ScriptsExplorerModel;

namespace NoQueryDatabase.Business.Implementation
{
    public class ScriptsExplorerBusinessService: IScriptsExplorerBusinessService
    {
        private readonly IScriptsExplorerDataService _scriptsExplorerDataService;
        private readonly ILogger<ScriptsExplorerBusinessService> _logger;

        public ScriptsExplorerBusinessService(IScriptsExplorerDataService scriptsExplorerDataService,
            ILogger<ScriptsExplorerBusinessService> logger)
        {
            _scriptsExplorerDataService = scriptsExplorerDataService;
            _logger = logger;
        }
        public async Task<List<DatabaseMetadata>> GetTables(string serverName, string authentication, string login, string password, string connectionString)
        {
            try
            {
                return await _scriptsExplorerDataService.GetTables(serverName, authentication, login, password, connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ScriptsExplorerBusinessService.GetTables");
                throw;
            }
        }
        public async Task<List<DatabaseMetadata>> GetStoredProcedures(string serverName, string authentication, string login, string password, string connectionString)
        {
            try
            {
                return await _scriptsExplorerDataService.GetStoredProcedures(serverName, authentication, login, password, connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ScriptsExplorerBusinessService.GetStoredProcedures");
                throw;
            }
        }
        public async Task<List<DatabaseMetadata>> GetFunctions(string serverName, string authentication, string login, string password, string connectionString)
        {
            try
            {
                return await _scriptsExplorerDataService.GetFunctions(serverName, authentication, login, password, connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ScriptsExplorerBusinessService.GetFunctions");
                throw;
            }
        }
        public async Task<List<DatabaseMetadata>> GetViews(string serverName, string authentication, string login, string password, string connectionString)
        {
            try
            {
                return await _scriptsExplorerDataService.GetViews(serverName, authentication, login, password, connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ScriptsExplorerBusinessService.GetViews");
                throw;
            }
        }
        //public async Task GenerateScripts(ScriptGenerationRequest request)
        //{
        //    try
        //    {
        //         await _scriptsExplorerDataService.GenerateScripts(request);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Exception in ScriptsExplorerBusinessService.GenerateScripts");
        //        throw;
        //    }
        //}
        
    }
}
