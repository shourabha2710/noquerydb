using NoQueryDatabase.Model.TableExplorerModel;

namespace NoQueryDatabase.Data.Contract
{
    public interface IMetadataProvider
    {
        Task<T> GetMetadataAsync<T>(string server, string database, string objectName, Func<Task<T>> factory);
        void Invalidate(string server, string database, string? objectName = null);
    }
}
