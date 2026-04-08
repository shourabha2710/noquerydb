namespace NoQueryDatabase.Model.DatabaseExplorerModel
{
    public class DatabaseMetadata
    {
        public string ServerName { get; set; }   // ← Add this if not already there
        public string Name { get; set; }         // Database name
        public List<string> Tables { get; set; } = new();
        public List<string> Views { get; set; } = new();
        public List<string> StoredProcedures { get; set; } = new();
        public List<string> Functions { get; set; } = new();
    }

}
