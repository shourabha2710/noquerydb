namespace NoQueryDB.Api.Models.Explorer
{
    public class TableConstraintDto
    {
        public string Name { get; set; }
        public string Type { get; set; }      // PRIMARY KEY, FOREIGN KEY, UNIQUE, CHECK, DEFAULT
        public string Columns { get; set; }
        public string Definition { get; set; }
    }

}
