using Microsoft.Extensions.Caching.Memory;
using NoQueryDatabase.Data.Contract;

namespace NoQueryDatabase.Data.Implementation
{
    public class MetadataProvider : IMetadataProvider
    {
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

        public MetadataProvider(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task<T> GetMetadataAsync<T>(string server, string database, string objectName, Func<Task<T>> factory)
        {
            var key = GenerateKey(server, database, objectName);

            if (_cache.TryGetValue(key, out T result))
            {
                return result;
            }

            result = await factory();

            if (result != null)
            {
                _cache.Set(key, result, DefaultExpiration);
            }

            return result;
        }

        public void Invalidate(string server, string database, string? objectName = null)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                // Note: IMemoryCache doesn't support wildcard invalidation easily without tracking keys.
                // For now, we only support specific object invalidation.
                // In a future update, we can implement a key-tracking mechanism.
            }
            else
            {
                var key = GenerateKey(server, database, objectName);
                _cache.Remove(key);
            }
        }

        private string GenerateKey(string server, string database, string objectName)
        {
            // Normalize inputs to ensure consistent keys
            var s = server.ToLowerInvariant().Replace(".", "_").Replace(":", "_");
            var d = database.ToLowerInvariant();
            var o = objectName.ToLowerInvariant();

            return $"{s}_{d}_{o}";
        }
    }
}
