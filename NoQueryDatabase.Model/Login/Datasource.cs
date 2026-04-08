using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoQueryDatabase.Model.Login
{
    public class Datasource
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        [MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(50)]
        public string Engine { get; set; } = "MSSQL";

        [MaxLength(200)]
        public string Server { get; set; } = null!;

        public int Port { get; set; }

        [MaxLength(200)]
        [JsonPropertyName("database")]
        public string DatabaseName { get; set; } = null!;

        [MaxLength(200)]
        public string? Username { get; set; }

        public string? EncryptedPassword { get; set; }

        public bool UseWindowsAuth { get; set; }
        public bool IsEncrypted { get; set; }

        public DateTime? DeletedAt { get; set; }
        public int? DeletedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
