using System.ComponentModel.DataAnnotations;
using ResolveIQ.Application.Validation;

namespace ResolveIQ.Application.DTOs.Auth
{
    public class RegisterRequest
    {
        [Required]
        [EmailWithDomain]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;
    }
}
