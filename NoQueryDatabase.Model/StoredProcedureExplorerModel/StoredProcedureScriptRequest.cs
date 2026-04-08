namespace NoQueryDatabase.Model.StoredProcedureExplorerModel
{
    public class StoredProcedureScriptRequest
    {
        public string ServerName { get; set; }
        public string DbName { get; set; }
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string ScriptType { get; set; } // "alter-script", "create-script", "drop-script", etc.
    }
}
