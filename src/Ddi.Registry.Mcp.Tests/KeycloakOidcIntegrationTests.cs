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

        [Fact]
        public async Task KeycloakPasswordToken_InitializesAndListsTools()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            factory.Seed();
            var client = await McpHttpTestClient.ConnectWithBearerTokenAsync(factory, await _fixture.GetPasswordTokenAsync());
            var tools = await client.ListToolsAsync();
            Assert.Equal(
                new[]
                {
                    "approve_concept",
                    "approve_representation",
                    "approve_variable",
                    "deprecate_concept",
                    "deprecate_representation",
                    "deprecate_variable",
                    "get_concept",
                    "get_representation",
                    "get_services",
                    "get_variable",
                    "get_variable_publishability",
                    "list_agencies",
                    "list_concepts",
                    "list_representations",
                    "list_variables",
                    "request_agency",
                    "request_concept",
                    "request_representation",
                    "request_variable",
                    "resolve_urn",
                    "update_concept_request",
                    "update_representation_request",
                    "update_variable_request",
                },
                tools.Select(tool => tool.Name).OrderBy(name => name));
        }

        [Fact]
        public async Task KeycloakPasswordToken_RequestAgency_MapsEmailToSeededLocalUser()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
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

        [Fact]
        public async Task KeycloakServiceToken_ReadToolSucceeds_WriteToolReturnsMissingScope()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            factory.Seed();
            var client = await McpHttpTestClient.ConnectWithBearerTokenAsync(factory, await _fixture.GetServiceTokenAsync());
            Assert.False((await client.CallToolAsync("list_agencies", new Dictionary<string, object>())).IsError);
            var write = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Denied", ["org"] = "us.denied" });
            Assert.Contains("Missing required scope", write.Content);
            Assert.Contains("ddi.registry.write", write.Content);
        }

        [Fact]
        public async Task KeycloakWrongAudienceToken_IsRejectedWithUnauthorized()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            var response = await SendInitializeAsync(factory, await _fixture.GetPasswordTokenAsync("wrong-audience-client"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task KeycloakUnauthenticatedRequest_IsRejectedWithUnauthorized()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
            using var factory = CreateFactory();
            var response = await McpHttpTestClient.SendInitializeAsync(factory.CreateClient(), "/mcp");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task KeycloakExpiredPasswordToken_IsRejectedWithUnauthorized()
        {
            Assert.SkipUnless(_fixture.Started, "Docker daemon is not available; skipping the Keycloak Testcontainer test.");
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