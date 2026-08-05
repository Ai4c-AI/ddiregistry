using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    [CollectionDefinition("Keycloak", DisableParallelization = true)]
    public class KeycloakCollection : ICollectionFixture<KeycloakFixture> { }

    [Collection("Keycloak")]
    public class KeycloakOidcIntegrationTests
    {
        private readonly KeycloakFixture _fixture;

        public KeycloakOidcIntegrationTests(KeycloakFixture fixture)
        {
            _fixture = fixture;
        }

        [SkippableFact]
        public async Task KeycloakPasswordToken_InitializesAndListsTools()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            factory.Seed();
            var client = await McpHttpTestClient.ConnectWithBearerTokenAsync(factory, await _fixture.GetPasswordTokenAsync());
            var tools = await client.ListToolsAsync();
            Assert.Equal(new[] { "get_services", "list_agencies", "request_agency", "resolve_urn" }, tools.Select(tool => tool.Name).OrderBy(name => name));
        }

        [SkippableFact]
        public async Task KeycloakPasswordToken_RequestAgency_MapsEmailToSeededLocalUser()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            factory.Seed();
            var client = await McpHttpTestClient.ConnectWithBearerTokenAsync(factory, await _fixture.GetPasswordTokenAsync());
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Keycloak Test", ["org"] = "us.keycloaktest" });
            Assert.Contains("Requested", result.Content);
            using var scope = factory.Services.CreateScope();
            var agency = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Agencies.FindAsync("us.keycloaktest");
            Assert.Equal(TestAuthHandler.SeedUserId, agency!.CreatorId);
            Assert.Equal(TestAuthHandler.SeedUserId, agency.AdminContactId);
            Assert.Equal(TestAuthHandler.SeedUserId, agency.TechnicalContactId);
        }

        [SkippableFact]
        public async Task KeycloakServiceToken_ReadToolSucceeds_WriteToolReturnsMissingScope()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            factory.Seed();
            var client = await McpHttpTestClient.ConnectWithBearerTokenAsync(factory, await _fixture.GetServiceTokenAsync());
            Assert.False((await client.CallToolAsync("list_agencies", new Dictionary<string, object>())).IsError);
            var write = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Denied", ["org"] = "us.denied" });
            Assert.Contains("Missing required scope", write.Content);
            Assert.Contains("ddi.registry.write", write.Content);
        }

        [SkippableFact]
        public async Task KeycloakWrongAudienceToken_IsRejectedWithUnauthorized()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            var response = await SendInitializeAsync(factory, await _fixture.GetPasswordTokenAsync("wrong-audience-client"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [SkippableFact]
        public async Task KeycloakUnauthenticatedRequest_IsRejectedWithUnauthorized()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            var response = await McpHttpTestClient.SendInitializeAsync(factory.CreateClient(), "/mcp");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [SkippableFact]
        public async Task KeycloakExpiredPasswordToken_IsRejectedWithUnauthorized()
        {
            Skip.IfNot(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            var token = await _fixture.GetPasswordTokenAsync("short-lived-client");
            await Task.Delay(TimeSpan.FromSeconds(6));
            var response = await SendInitializeAsync(factory, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        private McpWebApplicationFactory CreateFactory() => new(_fixture.Authority, "mcp-client");

        private static async Task<HttpResponseMessage> SendInitializeAsync(McpWebApplicationFactory factory, string accessToken)
        {
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await McpHttpTestClient.SendInitializeAsync(http, "/mcp");
        }
    }
}