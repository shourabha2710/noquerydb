using SendGrid;
using SendGrid.Helpers.Mail;

namespace NoQueryDB.Api.Helper
{
    public interface IEmailService
    {
        Task SendMailAsync(string to, string subject, string body);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendMailAsync(string to, string subject, string htmlBody)
        {
            var apiKey = _config["SendGrid:ApiKey"];

            if (string.IsNullOrEmpty(apiKey))
                throw new Exception("SendGrid API Key missing");

            var client = new SendGridClient(apiKey);

            var from = new EmailAddress(
                _config["SendGrid:From"],
                "NoQueryDB"
            );

            var msg = MailHelper.CreateSingleEmail(
                from,
                new EmailAddress(to),
                subject,
                plainTextContent: null,
                htmlContent: htmlBody
            );

            var response = await client.SendEmailAsync(msg);

            if ((int)response.StatusCode >= 400)
            {
                throw new Exception($"SendGrid failed: {response.StatusCode}");
            }
        }
    }
}
