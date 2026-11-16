using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using ResolveIQ.Infrastructure.Authentication;

namespace ResolveIQ.API.Authentication
{
    /// <summary>
    /// Runs after a token's signature and expiry check out. It makes sure the user behind the token
    /// still exists, is still active, and has not had their role or status changed since logging in.
    /// </summary>
    public static class TokenUserValidator
    {
        public static async Task ValidateAsync(TokenValidatedContext context)
        {
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser>>();

            var userId = context.Principal?.FindFirstValue("sub");
            var stamp = context.Principal?.FindFirstValue(JwtTokenService.StampClaim);

            var user = userId is null ? null : await userManager.FindByIdAsync(userId);

            if (user is null
                || stamp is null
                || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
                || await userManager.IsLockedOutAsync(user))
            {
                context.Fail("The token is no longer valid. Please log in again.");
            }
        }
    }
}
