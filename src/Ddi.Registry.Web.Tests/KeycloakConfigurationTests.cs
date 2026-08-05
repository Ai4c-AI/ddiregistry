using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ddi.Registry.Web.Tests;

public class KeycloakConfigurationTests
{
    [Fact]
    public async Task KeycloakCompleteConfiguration_RegistersExternalScheme()
    {
        await using var factory = new WebOidcApplicationFactory(configureKeycloak: true);
        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var schemes = await schemeProvider.GetAllSchemesAsync();

        Assert.Contains(schemes, scheme => scheme.Name == "Keycloak");
    }

    [Fact]
    public async Task KeycloakIncompleteConfiguration_DoesNotRegisterExternalScheme()
    {
        await using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var schemes = await schemeProvider.GetAllSchemesAsync();

        Assert.DoesNotContain(schemes, scheme => scheme.Name == "Keycloak");
    }

    [Fact]
    public void KeycloakDevelopmentConfiguration_AllowsHttpMetadata()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: true, environmentName: "Development");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Keycloak");

        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void KeycloakNonDevelopmentConfiguration_RequiresHttpsMetadata()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: true);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Keycloak");

        Assert.True(options.RequireHttpsMetadata);
    }
}