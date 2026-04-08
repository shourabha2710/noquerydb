namespace NoQueryDB.Api.Models.Datasource
{
    public class LoadDatabasesDto
    {
        public string Engine { get; set; } = "MSSQL";
        public string Server { get; set; } = "";
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseWindowsAuth { get; set; }
        public bool Encrypted { get; set; }
    }
}
