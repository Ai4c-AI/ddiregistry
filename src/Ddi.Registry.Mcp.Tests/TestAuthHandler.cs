using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Ddi.Registry.Mcp.Tests
{
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string HeaderName = "X-Test-Principal";
        public const string EmailClaim = "test@example.com";
        public const string SeedUserId = "mcp-test-user";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var principal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = principal.ToString() switch
            {
                "full" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "ddi.registry.read ddi.registry.write") },
                "read" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "ddi.registry.read") },
                // Authenticated but carries no scope claim at all (scope-denied path for read tools).
                "no-scope" => new[] { new Claim(ClaimTypes.Email, EmailClaim) },
                "unknown" => new[] { new Claim(ClaimTypes.Email, "unknown@example.com"), new Claim("scope", "ddi.registry.write") },
                "sub" => new[] { new Claim("sub", SeedUserId), new Claim("scope", "ddi.registry.write") },
                // Regression case: required scope is in the second same-name claim.
                "multi-read" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "unrelated"), new Claim("scope", "ddi.registry.read") },
                _ => Array.Empty<Claim>()
            };
            if (claims.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
