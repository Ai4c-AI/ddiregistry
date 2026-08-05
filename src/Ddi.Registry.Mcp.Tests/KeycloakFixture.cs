using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Testcontainers.Keycloak;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public sealed class KeycloakFixture : IAsyncLifetime
    {
        private KeycloakContainer? _container = null;

        public bool Started { get; private set; }
        public string Authority { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            try
            {
                var realmPath = Path.Combine(AppContext.BaseDirectory, "infra", "keycloak", "realm.json");
                var container = new KeycloakBuilder()
                    .WithImage("quay.io/keycloak/keycloak:26.0")
                    .WithUsername("admin")
                    .WithPassword("local-admin-password")
                    .WithBindMount(realmPath, "/opt/keycloak/data/import/realm.json")
                    .WithCommand("--import-realm")
                    .Build();
                _container = container;
                await container.StartAsync();
                Authority = $"{container.GetBaseAddress().TrimEnd('/')}/realms/ddi-registry";
                using var client = new HttpClient();
                var discovery = await client.GetAsync($"{Authority}/.well-known/openid-configuration");
                discovery.EnsureSuccessStatusCode();
                Started = true;
            }
            catch (DockerUnavailableException)
            {
                Started = false;
            }
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }

        public Task<string> GetPasswordTokenAsync(string clientId = "mcp-client") => GetTokenAsync(new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = "mcp-test-user",
            ["password"] = "local-test-password",
            ["scope"] = "openid ddi.registry.read ddi.registry.write"
        });

        public Task<string> GetServiceTokenAsync() => GetTokenAsync(new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "mcp-service-client",
            ["client_secret"] = "local-mcp-service-secret",
            ["scope"] = "ddi.registry.read"
        });

        private async Task<string> GetTokenAsync(System.Collections.Generic.Dictionary<string, string> fields)
        {
            using var client = new HttpClient();
            using var response = await client.PostAsync($"{Authority}/protocol/openid-connect/token", new FormUrlEncodedContent(fields));
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}