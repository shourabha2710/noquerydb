namespace NoQueryDB.Api.Helper
{
    public static class TurnstileHelper
    {
        public static async Task<bool> VerifyAsync(
            string token,
            string ip,
            string secret)
        {
            using var client = new HttpClient();

            var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secret,
                    ["response"] = token,
                    ["remoteip"] = ip
                })
            );

            var json = await response.Content.ReadAsStringAsync();
            return json.Contains("\"success\":true");
        }
    }

}
