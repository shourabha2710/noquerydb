using Microsoft.Extensions.Caching.Memory;

namespace NoQueryDB.Api.Service
{
    public interface IActiveDatasourceService
    {
        void SetActive(int userId, int datasourceId);
        int? GetActive(int userId);
        void Clear(int userId);
    }
    public class ActiveDatasourceService : IActiveDatasourceService
    {
        private readonly IMemoryCache _cache;

        public ActiveDatasourceService(IMemoryCache cache)
        {
            _cache = cache;
        }

        private string Key(int userId) => $"ACTIVE_DS_{userId}";

        public void SetActive(int userId, int datasourceId)
        {
            _cache.Set(Key(userId), datasourceId, TimeSpan.FromMinutes(30));
        }

        public int? GetActive(int userId)
        {
            return _cache.TryGetValue(Key(userId), out int dsId) ? dsId : null;
        }

        public void Clear(int userId)
        {
            _cache.Remove(Key(userId));
        }
    }
}
