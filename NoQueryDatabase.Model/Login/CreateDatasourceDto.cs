using System.ComponentModel.DataAnnotations;

namespace NoQueryDatabase.Model.Login
{
    public class CreateDatasourceDto : TestDatasourceDto
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;
    }
}
