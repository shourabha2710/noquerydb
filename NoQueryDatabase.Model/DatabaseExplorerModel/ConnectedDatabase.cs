using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Model.DatabaseExplorerModel
{
    public class ConnectedDatabase
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string Authentication { get; set; } // Windows or SQL
        public string Username { get; set; }
        public string Password { get; set; }
    }

}
