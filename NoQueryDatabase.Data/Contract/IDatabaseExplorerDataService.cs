using NoQueryDatabase.Model.DatabaseExplorerModel;

namespace NoQueryDatabase.Data.Contract
{
    public interface IDatabaseExplorerDataService
    {
        Task<List<DatabaseMetadata>> GetAllDatabaseMetadataAsync();
        Task<List<DatabaseMetadata>> ConnectNewDatabase(DynamicConnectionRequest dynamicConnectionRequest);
    }
}
