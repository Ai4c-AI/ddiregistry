using Ddi.Registry.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class ToolIntegrationTests
    {
        private static async Task<McpHttpTestClient> ConnectAsync(McpWebApplicationFactory factory, string? principal = "full")
        {
            var httpClient = factory.CreateClient();
            if (principal != null)
                httpClient.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, principal);
            var client = new McpHttpTestClient(httpClient, "/mcp");
            await client.InitializeAsync();
            return client;
        }

        [Fact]
        public async Task ToolsList_ReturnsExactlyTwentyThreeTools()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var tools = await client.ListToolsAsync();
            var names = tools.Select(t => t.Name).ToHashSet();
            Assert.Equal(new[]
            {
                "approve_concept",
                "approve_representation",
                "approve_variable",
                "deprecate_concept",
                "deprecate_representation",
                "deprecate_variable",
                "resolve_urn",
                "list_agencies",
                "get_services",
                "request_agency",
                "list_concepts",
                "get_concept",
                "list_representations",
                "get_representation",
                "list_variables",
                "get_variable",
                "get_variable_publishability",
                "request_concept",
                "request_representation",
                "request_variable",
                "update_concept_request",
                "update_representation_request"
                ,"update_variable_request"
            }.OrderBy(x => x), names.OrderBy(x => x));
            Assert.Equal(23, names.Count);
        }

        [Fact]
        public async Task Unauthenticated_Initialize_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            var response = await McpHttpTestClient.SendInitializeAsync(factory.CreateClient(), "/mcp");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", response.Headers.WwwAuthenticate.ToString());
            var metadata = await factory.CreateClient().GetAsync("/.well-known/oauth-protected-resource/mcp");
            Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
            Assert.Contains("ddi.registry.read", await metadata.Content.ReadAsStringAsync());
            Assert.Contains("ddi.registry.write", await metadata.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Initialize_Stateless_NoSessionIdHeader()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var httpClient = factory.CreateClient();
            httpClient.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "full");
            var response = await McpHttpTestClient.SendInitializeAsync(httpClient, "/mcp");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        }

        [Fact]
        public async Task ResolveUrn_AfterApproval_ReturnsFilledUrl()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:ddi:us.testorg:bar:1" });
            // 断言结果中含 https://us.testorg.example.org/bar
            Assert.Contains("https://us.testorg.example.org/bar", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_NonDdiScheme_ReturnsError()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:not-ddi:us.testorg:bar:1" });
            Assert.Contains("Cannot parse", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_DeprecatedAgency_IsNotResolved()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            // Use an isolated Deprecated agency so the shared seeded agency (us.testorg) is
            // never mutated — later tests depend on it remaining Approved.
            var deprecatedAgency = "us.deporg";
            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await context.Users.FindAsync(TestAuthHandler.SeedUserId);
                context.Agencies.Add(new Agency { AgencyId = deprecatedAgency, Label = "Deprecated Org", ApprovalState = ApprovalState.Deprecated, CreatorId = user!.Id, AdminContactId = user.Id, TechnicalContactId = user.Id });
                context.Assignments.Add(new Assignment { AssignmentId = deprecatedAgency, AgencyId = deprecatedAgency });
                context.HttpResolvers.Add(new HttpResolver { AssignmentId = deprecatedAgency, ResolutionType = HttpResolver.ServiceNameWeb, UrlTemplate = "https://{agency}.example.org/{identifier}" });
                await context.SaveChangesAsync();
            }
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = $"urn:ddi:{deprecatedAgency}:bar:1" });
            Assert.Contains("may not be approved", result.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_CreatesRequestedAgency()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "New Org", ["org"] = "us.neworg" });
            Assert.Contains("Requested", result.Content.ToString());
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull(await ctx.Agencies.FindAsync("us.neworg"));
        }

        [Fact]
        public async Task RequestAgency_Duplicate_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "us.dup" });
            var second = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "us.dup" });
            Assert.Contains("already exists", second.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_InvalidId_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "zz.bad" });
            Assert.Contains("not a valid country code", result.Content.ToString());
        }

        [Fact]
        public async Task IdentityMapping_UnknownEmail_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "unknown");
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Unknown", ["org"] = "us.unknown" });
            Assert.Contains("could not be mapped", result.Content.ToString());
        }

        [Fact]
        public async Task IdentityMapping_SubFallback_CreatesAgency()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var result = await (await ConnectAsync(factory, "sub")).CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Sub user", ["org"] = "us.subuser" });
            Assert.Contains("Requested", result.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_ReadOnlyScope_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Read only", ["org"] = "us.readonly" });
            Assert.Contains("Missing required scope", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_ScopeInSecondClaim_IsAccepted()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "multi-read");
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:ddi:us.testorg:bar:1" });
            Assert.Contains("https://us.testorg.example.org/bar", result.Content.ToString());
        }

        [Fact]
        public async Task ListAgencies_ScopeMissing_ReturnsError()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            // "no-scope" principal is authenticated but carries no scope claim.
            var client = await ConnectAsync(factory, "no-scope");
            var result = await client.CallToolAsync("list_agencies", new Dictionary<string, object>());
            Assert.Contains("Missing required scope", result.Content.ToString());
        }

        [Fact]
        public async Task GetServices_ReturnsServices()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("get_services", new Dictionary<string, object> { ["assignmentId"] = "us.testorg" });
            // Projection of the seeded Service row for us.testorg (hostname/serviceName match the
            // HttpResolver seeded alongside it).
            Assert.Contains("\"ok\":true", result.Content.ToString());
            Assert.Contains("\"hostname\":\"svc.us.testorg.example.org\"", result.Content.ToString());
            Assert.Contains("\"serviceName\":\"website\"", result.Content.ToString());
            Assert.Contains("\"port\":8080", result.Content.ToString());
        }

        [Fact]
        public async Task GetServices_UnknownAssignment_ReturnsEmpty()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("get_services", new Dictionary<string, object> { ["assignmentId"] = "nonexistent" });
            Assert.Contains("\"ok\":true", result.Content.ToString());
            Assert.Contains("\"services\":[]", result.Content.ToString());
        }
    }
}
