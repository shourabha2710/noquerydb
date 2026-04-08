namespace NoQueryDatabase.Model.Datasources
{
    public class ConnectDatasourceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int DatasourceId { get; set; }
    }
}
