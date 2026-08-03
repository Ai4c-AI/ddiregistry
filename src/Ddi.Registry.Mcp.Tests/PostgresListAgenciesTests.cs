using System.Collections.Generic;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    /// <summary>
    /// list_agencies country-filter behavior against a real PostgreSQL Testcontainer. The filter
    /// uses EF.Functions.ILike (Npgsql), which the EF InMemory provider cannot translate, so this
    /// path is exercised only against real Postgres — the same Testcontainer harness the 23505
    /// concurrency test uses. Requires a Docker daemon; skipped when Docker is unavailable.
    /// </summary>
    public sealed class PostgresListAgenciesTests : IAsyncLifetime
    {
        private PostgreSqlContainer? _container;

        private bool _started;

        public async Task InitializeAsync()
        {
            try
            {
                _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
                await _container.StartAsync();
                _started = true;
            }
            catch
            {
                // Docker daemon unavailable on the local machine; the [SkippableFact] below
                // reports the test as skipped rather than failing the whole suite.
                _started = false;
            }
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
                await _container.DisposeAsync();
        }

        [SkippableFact]
        public async Task ListAgencies_CountryFilter_ReturnsMatching()
        {
            Skip.If(_container is null, "Docker not available");
            Skip.IfNot(_started, "Docker daemon is not available; skipping the PostgreSQL Testcontainer test.");

            using var factory = new McpWebApplicationFactory { ConnectionString = _container!.GetConnectionString() };
            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();
            }
            // Seeds the local user plus the Approved us.testorg agency/assignment/service.
            factory.Seed();

            // A second agency that must NOT match the "us" country prefix filter.
            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await context.Users.FindAsync(TestAuthHandler.SeedUserId);
                context.Agencies.Add(new Agency
                {
                    AgencyId = "uk.testorg",
                    Label = "UK Test Org",
                    ApprovalState = ApprovalState.Approved,
                    CreatorId = user!.Id,
                    AdminContactId = user.Id,
                    TechnicalContactId = user.Id
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory);
            var result = await client.CallToolAsync("list_agencies", new Dictionary<string, object> { ["country"] = "us" });
            var content = result.Content.ToString();
            Assert.Contains("\"agencyId\":\"us.testorg\"", content);
            Assert.DoesNotContain("uk.testorg", content);
        }
    }
}
