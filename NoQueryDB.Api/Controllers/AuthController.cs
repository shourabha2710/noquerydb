using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoQueryDB.Api.Helper;
using NoQueryDatabase.Model.Login;
using NoQueryDB.Api.DatabaseContext;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using static NoQueryDB.Api.Service.EncryptionService;
using Microsoft.Data.SqlClient;
using NoQueryDB.Api.Service;
using Newtonsoft.Json.Linq;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthController(
            AppDbContext db,
            IConfiguration config,
            IEmailService emailService,
            IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _config = config;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
        }

        // ✅ STEP 1: REQUEST LOGIN
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("request-login")]
        public async Task<IActionResult> RequestLogin([FromBody] LoginRequestDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Email))
                    return BadRequest("Email required");

                

                if (string.IsNullOrWhiteSpace(dto.SessionId))
                    return BadRequest("SessionId required");

               
                var turnstileEnabled = _config.GetValue<bool>("Turnstile:Enabled");


                try
                {
                    if (turnstileEnabled)
                    {
                        if (string.IsNullOrWhiteSpace(dto.TurnstileToken))
                            return BadRequest("Human verification failed");
                        var secret = _config["Turnstile:SecretKey"];
                        if (string.IsNullOrWhiteSpace(secret))
                            return StatusCode(500, "Turnstile secret key not configured");
                        // VERIFY CLOUDFLARE TURNSTILE
                        var client = _httpClientFactory.CreateClient();
                        var response = await client.PostAsync(
                            "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                            new FormUrlEncodedContent(new Dictionary<string, string>
                            {
                    { "secret", _config["Turnstile:SecretKey"] },
                    { "response", dto.TurnstileToken }
                            })
                        );

                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<TurnstileResponse>(json);

                        if (result == null || !result.success)
                            return BadRequest("Human verification failed");
                    }
                    
                }
                catch (HttpRequestException ex)
                {
                    return StatusCode(503, ex.InnerException?.Message ?? ex.Message);
                }


                // Remove existing tokens/sessions for this email (single active token/session)
                var oldTokens = _db.LoginRequests.Where(x => x.Email == dto.Email);
                _db.LoginRequests.RemoveRange(oldTokens);
                await _db.SaveChangesAsync();

                // Generate secure token (rawToken is the one sent in email)
                var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var hashedToken = HashToken(rawToken);

                var login = new LoginRequest
                {
                    Email = dto.Email,
                    Token = hashedToken,
                    SessionId = dto.SessionId,
                    IsVerified = false,
                    IsCompleted = false,
                    JwtToken = null,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                };

                _db.LoginRequests.Add(login);
                await _db.SaveChangesAsync();

                // Email should contain frontend verify route with raw token
                var verifyUrl =
        $"{Request.Scheme}://{Request.Host}/api/auth/verify?token={rawToken}";

                // use mobile-safe email template (table + anchor)
                try
                {
                    await _emailService.SendMailAsync(
                    dto.Email,
                    "Sign in to NoQueryDB",
        $@"
    <div style='font-family:Arial,Helvetica,sans-serif;max-width:600px;margin:0 auto;'>

      <h2>NoQueryDB Login</h2>

      <p>You requested to sign in to <b>NoQueryDB</b>.</p>

      <!-- ✅ iOS / Gmail SAFE BUTTON -->
      <table width='100%' cellspacing='0' cellpadding='0'>
        <tr>
          <td align='center' style='padding:20px'>
            <a href='{verifyUrl}'
               target='_blank'
               style='
                 background:#000000;
                 color:#ffffff !important;
                 padding:14px 24px;
                 text-decoration:none;
                 border-radius:6px;
                 display:inline-block;
                 font-size:15px;
                 font-family:Arial,Helvetica,sans-serif;
                 -webkit-text-size-adjust:none;
               '>
              Verify Email
            </a>
          </td>
        </tr>
      </table>

      <p style='font-size:13px;color:#333;margin-top:10px;'>
        Or copy and paste this link into your browser:
      </p>

      <p style='font-size:12px;word-break:break-all;'>
        <a href='{verifyUrl}' style='color:#1a73e8;'>{verifyUrl}</a>
      </p>

      <p style='font-size:12px;color:#666;margin-top:20px;'>
        This link will expire in 15 minutes.
      </p>

    </div>"
                );
                }
                catch (Exception ex) 
                {
                    return StatusCode(500, ex.Message);
                }
                    

                // Return ok (frontend will poll)
                return Ok(new { message = "Verification email sent" });

            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }

        }

        // ✅ STEP 2: VERIFY LINK CLICK
        [HttpGet("verify")]
        public async Task<IActionResult> VerifyLogin(string token)
        {
            var hashed = HashToken(token);

            var record = await _db.LoginRequests.FirstOrDefaultAsync(x =>
                x.Token == hashed &&
                !x.IsVerified &&
                x.ExpiresAt > DateTime.UtcNow);

            if (record == null)
                return BadRequest("Invalid or expired token");

            record.IsVerified = true;
            record.IsCompleted = true;
            var user = await _db.Users.FirstOrDefaultAsync(x =>
    x.Email == record.Email && x.IsActive);

            if (user == null)
                return BadRequest("User not registered");

            record.JwtToken = JwtHelper.GenerateToken(
    user.Id,
    user.Email,
    user.Role,
    user.CompanyId,
    _config
);

            await _db.SaveChangesAsync();

            // ✅ redirect to frontend AFTER backend verification
            return Redirect(
      $"{_config["FrontendUrl"]}/verified-success?sessionId={record.SessionId}"
    );
        }

        // ✅ STEP 3: DEVICE POLLING FOR JWT
        [HttpGet("check-login")]
        public async Task<IActionResult> CheckLogin(string sessionId)
        {
            var record = await _db.LoginRequests.FirstOrDefaultAsync(x =>
                x.SessionId == sessionId &&
                x.IsCompleted &&
                x.JwtToken != null);

            if (record == null)
                return NoContent();

            var token = record.JwtToken;
            var email = record.Email;

            // ✅ invalidate after first use
            record.JwtToken = null;
            _db.LoginRequests.Remove(record);
            await _db.SaveChangesAsync();

            return Ok(new { token, email });
        }
        private static string HashToken(string token)
        {
            return Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(token))
            );
        }
    }

}
