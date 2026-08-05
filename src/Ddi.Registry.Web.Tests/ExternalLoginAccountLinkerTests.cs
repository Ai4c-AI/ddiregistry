using System.Security.Claims;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ddi.Registry.Web.Tests;

public class ExternalLoginAccountLinkerTests
{
    [Fact]
    public async Task KeycloakExistingEmail_BindsExternalLogin()
    {
        await using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        await factory.SeedUserAsync("test@example.com");
        using var scope = factory.Services.CreateScope();
        var linker = scope.ServiceProvider.GetRequiredService<ExternalLoginAccountLinker>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(), "Keycloak", "keycloak-subject", "Keycloak");

        var result = await linker.LinkAsync(loginInfo, "test@example.com");

        Assert.Equal(ExternalLoginLinkResult.Linked, result);
        var user = await userManager.FindByEmailAsync("test@example.com");
        var logins = await userManager.GetLoginsAsync(user!);
        Assert.Contains(logins, login => login.LoginProvider == "Keycloak" && login.ProviderKey == "keycloak-subject");
    }

    [Fact]
    public async Task KeycloakUnknownEmail_DoesNotCreateLocalUser()
    {
        await using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var scope = factory.Services.CreateScope();
        var linker = scope.ServiceProvider.GetRequiredService<ExternalLoginAccountLinker>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(), "Keycloak", "keycloak-subject", "Keycloak");

        var result = await linker.LinkAsync(loginInfo, "unknown@example.com");

        Assert.Equal(ExternalLoginLinkResult.MissingUser, result);
        Assert.Null(await userManager.FindByEmailAsync("unknown@example.com"));
    }
}