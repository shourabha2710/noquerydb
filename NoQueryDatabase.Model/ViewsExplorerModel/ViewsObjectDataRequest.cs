using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoQueryDatabase.Model.ViewExplorerModel
{
    public class ViewsObjectDataRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string FilterColumn { get; set; }
        public string FilterValue { get; set; }
        public string SortOrder { get; set; }
        public string FilterOperator { get; set; }
    }
}
