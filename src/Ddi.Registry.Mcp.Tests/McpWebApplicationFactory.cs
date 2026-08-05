using Ddi.Registry.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ddi.Registry.Mcp.Tests
{
    /// <summary>
    /// Boots the real MCP host (WebApplicationFactory&lt;Program&gt;) with authentication enabled.
    /// The real Postgres DbContext is swapped for EF InMemory (unless a ConnectionString is supplied,
    /// as the 23505 concurrency test requires real Postgres), and a request-header-driven Test
    /// authentication scheme is installed. Seeding writes a local user, an Approved agency with an
    /// Assignment + HttpResolver + Service, matching the resolution used by the MCP tools.
    /// </summary>
    public class McpWebApplicationFactory : WebApplicationFactory<Program>
    {
        /// <summary>When set, the factory uses this Npgsql connection string instead of EF InMemory.</summary>
        public string? ConnectionString { get; init; }
        public bool UseRealOidc { get; }
        public string? OidcAuthority { get; }
        public string? OidcAudience { get; }

        public McpWebApplicationFactory(string? oidcAuthority = null, string? oidcAudience = null)
        {
            // Program.cs reads MCP:Oidc:* and MCP:ReverseProxy:* in its top-level statements, before
            // WebApplicationFactory's ConfigureAppConfiguration hook is applied. Environment variables
            // (with __ as the section separator) are loaded by WebApplication.CreateBuilder before any
            // top-level read, so they are the reliable channel for enabling authentication in tests.
            UseRealOidc = !string.IsNullOrWhiteSpace(oidcAuthority);
            OidcAuthority = oidcAuthority;
            OidcAudience = oidcAudience;
            SetEnvironmentVariable("MCP__Oidc__Authority", oidcAuthority ?? "https://test-idp.invalid");
            SetEnvironmentVariable("MCP__Oidc__Audience", oidcAudience ?? "mcp-test-audience");
            SetEnvironmentVariable("MCP__Oidc__Scopes", "ddi.registry.read ddi.registry.write");
            SetEnvironmentVariable("MCP__ReverseProxy__TrustedProxy", "127.0.0.1");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(UseRealOidc ? "Development" : "Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                if (ConnectionString is null)
                    services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("mcp-test"));
                else
                    services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
                if (!UseRealOidc)
                {
                    // Program's fallback policy has no named scheme. Override only
                    // authentication for tests; retain McpAuth as the challenge scheme.
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = "McpAuth";
                    })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                }
                else
                {
                    services.Configure<JwtBearerOptions>("Bearer", options => options.TokenValidationParameters.ClockSkew = TimeSpan.Zero);
                }
            });
        }

        private static void SetEnvironmentVariable(string name, string value)
            => Environment.SetEnvironmentVariable(name, value);

        public void Seed()
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (ctx.Users.Any()) return;
            var user = new ApplicationUser { Id = TestAuthHandler.SeedUserId, UserName = "tester", Email = TestAuthHandler.EmailClaim, NormalizedEmail = TestAuthHandler.EmailClaim.ToUpperInvariant() };
            ctx.Users.Add(user);
            var agencyId = "us.testorg";
            ctx.Agencies.Add(new Agency { AgencyId = agencyId, Label = "Test Org", ApprovalState = ApprovalState.Approved, CreatorId = user.Id, AdminContactId = user.Id, TechnicalContactId = user.Id });
            ctx.Assignments.Add(new Assignment { AssignmentId = agencyId, AgencyId = agencyId });
            ctx.HttpResolvers.Add(new HttpResolver { AssignmentId = agencyId, ResolutionType = HttpResolver.ServiceNameWeb, UrlTemplate = "https://{agency}.example.org/{identifier}" });
            ctx.Services.Add(new Service
            {
                AssignmentId = agencyId,
                Hostname = "svc.us.testorg.example.org",
                Port = 8080,
                ServiceName = HttpResolver.ServiceNameWeb,
                Protocol = "tcp",
                Priority = 1,
                Weight = 10,
                TimeToLive = 300
            });
            ctx.SaveChanges();
        }
    }
}
