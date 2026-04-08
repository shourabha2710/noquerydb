using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Model.TableExplorerModel
{
    public class TableAlterQueryGenerateRequest
    {
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public List<ColumnAlterModel> Columns { get; set; } = new();
    }
    public class ColumnAlterModel
    {
        public string Action { get; set; }        // ADD, MODIFY, DROP
        public string Name { get; set; }
        public string NewName { get; set; }
        public string DataType { get; set; }
        public string Length { get; set; }
        public string Nullable { get; set; }      // YES/NO
        public string DefaultValue { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
    }
}
