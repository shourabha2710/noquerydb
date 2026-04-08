namespace NoQueryDB.Api.Models.Explorer
{
    public class ForeignKeyEdgeDto
    {
        public string FromTable { get; set; }
        public string FromColumn { get; set; }
        public string ToTable { get; set; }
        public string ToColumn { get; set; }
        public string Name { get; set; }
    }
}
