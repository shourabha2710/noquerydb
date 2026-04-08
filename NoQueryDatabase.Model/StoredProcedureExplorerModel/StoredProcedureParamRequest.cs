namespace NoQueryDatabase.Model.StoredProcedureExplorerModel
{
    public class StoredProcedureParamRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
    }
}
