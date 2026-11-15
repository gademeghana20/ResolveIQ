using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResolveIQ.Infrastructure.Authentication;

namespace ResolveIQ.Tests.Authentication
{
    public class JwtTokenServiceTests
    {
        private static readonly JwtSettings Settings = new()
        {
            Key = "unit-test-key-that-is-definitely-longer-than-32-characters!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiryMinutes = 60
        };

        private static TokenValidationParameters ValidationParameters(string key) => new()
        {
            ValidateIssuer = true,
            ValidIssuer = Settings.Issuer,
            ValidateAudience = true,
            ValidAudience = Settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        private static string CreateToken(params string[] roles) =>
            new JwtTokenService(Options.Create(Settings))
                .CreateToken("user-123", "jane@example.com", roles);

        [Fact]
        public async Task CreateToken_ProducesTokenThatValidatesWithUserIdEmailAndRoles()
        {
            var token = CreateToken("Manager", "Employee");

            var result = await new JsonWebTokenHandler()
                .ValidateTokenAsync(token, ValidationParameters(Settings.Key));

            Assert.True(result.IsValid);
            Assert.Equal("user-123", result.ClaimsIdentity.Name);
            Assert.True(result.ClaimsIdentity.HasClaim("email", "jane@example.com"));
            Assert.True(result.ClaimsIdentity.HasClaim("role", "Manager"));
            Assert.True(result.ClaimsIdentity.HasClaim("role", "Employee"));
            Assert.False(result.ClaimsIdentity.HasClaim("role", "Admin"));
        }

        [Fact]
        public async Task CreateToken_IsRejectedWhenSignedWithDifferentKey()
        {
            var token = CreateToken("Admin");

            var result = await new JsonWebTokenHandler().ValidateTokenAsync(
                token, ValidationParameters("a-completely-different-key-also-longer-than-32-chars!!"));

            Assert.False(result.IsValid);
        }
    }
}
