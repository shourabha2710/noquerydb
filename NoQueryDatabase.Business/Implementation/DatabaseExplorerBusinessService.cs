using Microsoft.Extensions.Logging;
using NoQueryDatabase.Business.Contract;
using NoQueryDatabase.Data.Contract;
using NoQueryDatabase.Model.DatabaseExplorerModel;

namespace NoQueryDatabase.Business.Implementation
{
    public class DatabaseExplorerBusinessService: IDatabaseExplorerBusinessService
    {
        private readonly IDatabaseExplorerDataService _databaseExplorerDataService;
        private readonly ILogger<DatabaseExplorerBusinessService> _logger;

        public DatabaseExplorerBusinessService(IDatabaseExplorerDataService databaseExplorerDataService,
            ILogger<DatabaseExplorerBusinessService> logger)
        {
            _databaseExplorerDataService = databaseExplorerDataService;
            _logger = logger;
        }
        public async Task<List<DatabaseMetadata>> GetAllDatabaseMetadataAsync()
        {
            try
            {
                return await _databaseExplorerDataService.GetAllDatabaseMetadataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in DatabaseExplorerBusinessService.GetAllDatabaseMetadataAsync");
                throw;
            }
        }
        public async Task<List<DatabaseMetadata>> ConnectNewDatabase(DynamicConnectionRequest dynamicConnectionRequest)
        {
            try
            {
                return await _databaseExplorerDataService.ConnectNewDatabase(dynamicConnectionRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in DatabaseExplorerBusinessService.ConnectNewDatabase");
                throw;
            }
        }
    }
}
