using NoQueryDatabase.Model.DatabaseExplorerModel;

namespace NoQueryDatabase.Business.Contract
{
    public interface IDatabaseExplorerBusinessService
    {
        Task<List<DatabaseMetadata>> GetAllDatabaseMetadataAsync();
        Task<List<DatabaseMetadata>> ConnectNewDatabase(DynamicConnectionRequest dynamicConnectionRequest);
    }
}
