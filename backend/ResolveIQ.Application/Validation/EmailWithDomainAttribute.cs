using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ResolveIQ.Application.Validation
{
    /// <summary>
    /// An email address whose part after the @ is a full domain, such as example.com.
    /// The standard [EmailAddress] check also accepts "name@example" (no ending), which is technically
    /// legal inside a company network but on the public internet is nearly always a typo, and such an
    /// account could never receive a password-reset email. This attribute adds that missing requirement.
    /// Use it where an address is first accepted (registration, creating a user), not on sign-in, so that
    /// older accounts keep working.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class EmailWithDomainAttribute : ValidationAttribute
    {
        public const string DefaultMessage = "Enter a full email address, like name@example.com.";

        // Something, an @, then at least one "label." and an ending of two or more characters. No spaces, no empty parts.
        private static readonly Regex Shape = new(
            @"^[^\s@]+@([^\s@.]+\.)+[^\s@.]{2,}$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        public EmailWithDomainAttribute()
            : base(DefaultMessage)
        {
        }

        // A missing value is left to [Required], the same way the built-in checks work.
        public override bool IsValid(object? value) =>
            value is null || (value is string text && IsValidEmail(text));

        public static bool IsValidEmail(string text) =>
            new EmailAddressAttribute().IsValid(text) && Shape.IsMatch(text);
    }
}
