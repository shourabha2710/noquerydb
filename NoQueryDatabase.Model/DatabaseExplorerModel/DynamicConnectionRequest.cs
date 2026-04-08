using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Model.DatabaseExplorerModel
{
    public class DynamicConnectionRequest
    {
        public string ServerName { get; set; }
        public string Authentication { get; set; } // "Windows" or "SQL"
        public string Login { get; set; }
        public string Password { get; set; }
        public string ConnectionString { get; set; } // Used if provided directly
        public bool RememberPassword { get; set; } // Optional for local storage
    }
}
