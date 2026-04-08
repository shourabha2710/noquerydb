namespace NoQueryDB.Api.Models.Explorer
{
    public class SavedQuery
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int CompanyId { get; set; }
        public string Name { get; set; }
        public string SqlText { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
