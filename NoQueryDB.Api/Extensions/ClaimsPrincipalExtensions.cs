using System.Security.Claims;

namespace NoQueryDB.Api.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user)
        {
            return int.Parse(
                user.FindFirstValue("userId")
                ?? throw new UnauthorizedAccessException("userId missing"));
        }

        public static int GetCompanyId(this ClaimsPrincipal user)
        {
            return int.Parse(
                user.FindFirstValue("companyId")
                ?? throw new UnauthorizedAccessException("companyId missing"));
        }
    }
}
