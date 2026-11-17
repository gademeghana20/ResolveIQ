namespace ResolveIQ.Application.DTOs.Auth
{
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;

        /// <summary>A single-use ticket for getting a new Token when this one expires, without typing the password again.</summary>
        public string RefreshToken { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public IList<string> Roles { get; set; } = new List<string>();
    }
}
