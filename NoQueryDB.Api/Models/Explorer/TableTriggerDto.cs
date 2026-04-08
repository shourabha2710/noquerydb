namespace NoQueryDB.Api.Models.Explorer
{
    public class TableTriggerDto
    {
        public string Name { get; set; }
        public string Type { get; set; }      // AFTER / INSTEAD OF
        public string Events { get; set; }    // INSERT, UPDATE, DELETE
        public bool IsEnabled { get; set; }
        public string Definition { get; set; }
    }

}
