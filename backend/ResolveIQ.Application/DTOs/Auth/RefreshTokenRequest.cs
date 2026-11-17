using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Auth
{
    /// <summary>The refresh token the app was given at sign-in, sent back to get a new login token (or to sign out).</summary>
    public class RefreshTokenRequest
    {
        [Required]
        [StringLength(200)]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
