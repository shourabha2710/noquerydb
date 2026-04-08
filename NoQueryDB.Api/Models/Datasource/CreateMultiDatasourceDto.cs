namespace NoQueryDB.Api.Models.Datasource
{
    public class CreateMultiDatasourceDto
    {
        public string Name { get; set; } = "";
        public string Engine { get; set; } = "MSSQL";
        public string Server { get; set; } = "";
        public int Port { get; set; }
        public List<string> Databases { get; set; } = new();
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseWindowsAuth { get; set; }
        public bool Encrypted { get; set; }
    }
}
