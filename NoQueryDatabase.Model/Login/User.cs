namespace NoQueryDatabase.Model.Login
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;   // SystemAdmin / CompanyAdmin / CompanyEmployee
        public int CompanyId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
