using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ddi.Registry.Web.Tests;

public sealed class WebOidcApplicationFactory : WebApplicationFactory<Program>
{
    private readonly bool _configureKeycloak;
    private readonly string _environmentName;
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public WebOidcApplicationFactory(bool configureKeycloak, string environmentName = "Testing")
    {
        _configureKeycloak = configureKeycloak;
        _environmentName = environmentName;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>();
            if (_configureKeycloak)
            {
                settings["Authentication:Keycloak:Authority"] = "https://keycloak.test/realms/ddi-registry";
                settings["Authentication:Keycloak:ClientId"] = "registry-web";
                settings["Authentication:Keycloak:ClientSecret"] = "test-secret";
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDatabaseProvider>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task SeedUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}