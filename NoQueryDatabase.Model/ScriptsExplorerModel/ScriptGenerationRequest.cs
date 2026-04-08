namespace NoQueryDatabase.Model.ScriptsExplorerModel
{
    public class ScriptGenerationRequest
    {
        public List<ScriptObjectSelection> Objects { get; set; } = new();
        public string SaveAs { get; set; } // script | query
        public string FilesMode { get; set; } // Single script file | One file per object
        public string Path { get; set; }
        public bool Overwrite { get; set; }
    }

    public class ScriptObjectSelection
    {
        public string Database { get; set; }
        public string TableName { get; set; }
        public string ScriptAction { get; set; }
        public string DataScript { get; set; }
        public List<string> Options { get; set; }
    }

}
