using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Auth
{
    public class LoginRequest
    {
        // Deliberately the lenient check: accounts created before the stricter registration rule, such as
        // "name@example" with no ending, must still be able to sign in. Only new addresses are held to it.
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;
    }
}
