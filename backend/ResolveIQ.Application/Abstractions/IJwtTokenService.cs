namespace ResolveIQ.Application.Abstractions
{
    public interface IJwtTokenService
    {
        /// <summary>
        /// Creates a signed JWT for a user, containing their id, email, roles and security stamp.
        /// The stamp lets the API reject the token as soon as the user is deactivated or their role changes.
        /// </summary>
        string CreateToken(string userId, string email, IEnumerable<string> roles, string securityStamp);
    }
}
