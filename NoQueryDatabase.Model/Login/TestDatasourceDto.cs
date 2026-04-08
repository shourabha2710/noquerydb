using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoQueryDatabase.Model.Login
{
    public class TestDatasourceDto
    {
        [Required]
        public string Engine { get; set; } = "MSSQL";

        [Required]
        public string Server { get; set; } = null!;

        [Range(1, 65535)]
        public int Port { get; set; } = 1433;

        // ✅ NOT REQUIRED anymore
        [JsonPropertyName("database")]
        public string? DatabaseName { get; set; }

        public string? Username { get; set; }
        public string? Password { get; set; }

        public bool UseWindowsAuth { get; set; }
        public bool Encrypted { get; set; }
    }
}
