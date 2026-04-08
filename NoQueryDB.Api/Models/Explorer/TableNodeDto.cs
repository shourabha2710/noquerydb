namespace NoQueryDB.Api.Models.Explorer
{
    public class TableNodeDto
    {
        public string Name { get; set; }
        public List<string> Columns { get; set; } = new();
    }
}
