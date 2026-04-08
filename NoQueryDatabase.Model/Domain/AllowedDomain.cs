namespace NoQueryDatabase.Model.Domain
{
    public class AllowedDomain
    {
        public int Id { get; set; }
        public string Domain { get; set; } = null!;        // ex: "gmail.com"
        public bool IsCorporate { get; set; } = false;     // mark corporate-only domains
        public bool IsActive { get; set; } = true;        // enable/disable
    }
}
