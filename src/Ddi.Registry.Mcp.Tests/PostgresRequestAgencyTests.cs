using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    /// <summary>
    /// Concurrent request_agency test against a real PostgreSQL Testcontainer. InMemory cannot
    /// reproduce the Postgres unique-constraint violation, so this test boots the host against the
    /// container and relies on Npgsql.PostgresException 23505 to cover the
    /// IsAgencyPrimaryKeyViolation branch in RegistryTools. Requires a Docker daemon; when Docker is
    /// not available the test is skipped (CI runs with Docker, so the branch is covered there).
    /// </summary>
    public sealed class PostgresRequestAgencyTests : IAsyncLifetime
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
        public async Task ConcurrentRequestAgency_SameOrg_ExactlyOneSucceeds()
        {
            Skip.If(_container is null, "Docker not available");
            Skip.IfNot(_started, "Docker daemon is not available; skipping the PostgreSQL Testcontainer test.");

            using var factory = new McpWebApplicationFactory { ConnectionString = _container!.GetConnectionString() };
            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();
            }
            // Seeds the local user (identity mapping target) plus the Approved us.testorg fixture.
            factory.Seed();

            var clientA = await McpHttpTestClient.ConnectAsync(factory);
            var clientB = await McpHttpTestClient.ConnectAsync(factory);

            const string org = "us.race";
            var results = await Task.WhenAll(
                clientA.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Race Org", ["org"] = org }),
                clientB.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Race Org", ["org"] = org }));

            // Exactly one concurrent request wins; the loser reports the duplicate, either via the
            // pre-check or (when the inserts overlap) via the 23505 PK_Agencies violation.
            Assert.Equal(1, results.Count(r => r.Content.Contains("Requested")));
            Assert.Equal(1, results.Count(r => r.Content.Contains("already exists")));
        }
    }
}
