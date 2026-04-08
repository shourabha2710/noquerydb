using NoQueryDatabase.Model.DatabaseExplorerModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Utility
{
    public static class ConnectionStore
    {
        private static readonly Dictionary<string, ConnectedDatabase> _connections = new();

        public static void AddOrUpdate(string key, ConnectedDatabase conn) => _connections[key] = conn;

        public static ConnectedDatabase Get(string key)
            => _connections.TryGetValue(key, out var conn) ? conn : null;
    }

}
