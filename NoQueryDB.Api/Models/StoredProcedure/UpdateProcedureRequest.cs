namespace NoQueryDB.Api.Models.StoredProcedure
{
    public class UpdateProcedureRequest
    {
        public string Schema { get; set; }
        public string Procedure { get; set; }
        public string Sql { get; set; }
    }

}
