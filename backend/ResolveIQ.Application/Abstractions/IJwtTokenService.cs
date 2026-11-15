namespace ResolveIQ.Application.Abstractions
{
    public interface IJwtTokenService
    {
        /// <summary>
        /// Creates a signed JWT for a user, containing their id, email and roles.
        /// </summary>
        string CreateToken(string userId, string email, IEnumerable<string> roles);
    }
}
