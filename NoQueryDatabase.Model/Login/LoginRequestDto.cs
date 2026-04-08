namespace NoQueryDatabase.Model.Login
{
    public class LoginRequestDto
    {
        public string Email { get; set; }
        public string? TurnstileToken { get; set; }
        public string SessionId { get; set; }
    }
}
