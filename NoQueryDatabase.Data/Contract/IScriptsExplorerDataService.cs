using NoQueryDatabase.Model.DatabaseExplorerModel;
using NoQueryDatabase.Model.ScriptsExplorerModel;

namespace NoQueryDatabase.Data.Contract
{
    public interface IScriptsExplorerDataService
    {
        Task<List<DatabaseMetadata>> GetTables(string serverName, string authentication, string login, string password, string connectionString);
        Task<List<DatabaseMetadata>> GetStoredProcedures(string serverName, string authentication, string login, string password, string connectionString);
        Task<List<DatabaseMetadata>> GetFunctions(string serverName, string authentication, string login, string password, string connectionString);
        Task<List<DatabaseMetadata>> GetViews(string serverName, string authentication, string login, string password, string connectionString);
        //Task GenerateScripts(ScriptGenerationRequest request);
    }
}
