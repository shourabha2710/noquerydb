using NoQueryDatabase.Model.StoredProcedureExplorerModel;
using System.Data;

namespace NoQueryDatabase.Business.Contract
{
    public interface IStoredProcedureExplorerBusinessService
    {
        public Task<List<string>> GetStoredProcedureParameters(string StoredProcedureName, string ServerName, string DbName);
        public Task<(DataTable, int, Dictionary<string, string>, long, long, long, List<int>)> ExecuteStoredProcedure(StoredProcedureExecuteRequest storedProcedureExecuteRequest);
        public Task<(DataTable, int, Dictionary<string, string>, long, long, long, List<int>)> ExecuteSearchStoredProcedure(StoredProcedureExecuteRequest storedProcedureExecuteRequest);
        public Task<string> GenerateStoredProcedureScriptAsync(StoredProcedureScriptRequest storedProcedureScriptRequest);
    }
}
