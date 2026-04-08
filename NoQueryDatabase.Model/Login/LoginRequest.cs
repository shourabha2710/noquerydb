using System.ComponentModel.DataAnnotations;

namespace NoQueryDatabase.Model.Login
{
    public class LoginRequest
    {
        public int Id { get; set; }
        public string Email { get; set; } = null!;
        public string Token { get; set; } = null!;     // hashed token
        public string SessionId { get; set; } = null!; // new
        public bool IsVerified { get; set; }
        public bool IsCompleted { get; set; }          // new: whether verification completed
        public string? JwtToken { get; set; }          // new: store JWT generated at verify time
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }


}
