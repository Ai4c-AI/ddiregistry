using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Controllers;
using Ddi.Registry.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Ddi.Registry.Web.Tests;

// Covers the "Manage 视图/控制器 ModelState 错误路径" test gap flagged by code review:
// verifies AgencyIdValidator error codes render as Chinese text (not English or key names)
// under zh-CN ambient culture, using the real IStringLocalizer<ManageController> from DI.
public sealed class ManageControllerLocalizationTests
{
    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"web-manage-localization-{System.Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void WithCulture(string culture, Action action)
    {
        var current = CultureInfo.CurrentUICulture;
        var currentCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = current;
            CultureInfo.CurrentCulture = currentCulture;
        }
    }

    [Fact]
    public async Task AddAgency_InvalidAgencyPrefixWithChineseCulture_AddsChineseModelError()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var scope = factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<ManageController>>();

        await using var context = CreateContext();
        var controller = new ManageController(context, null!, null!, new NoOpEmailSender(), localizer);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1")
                ], "Test"))
            }
        };

        // 直接调用控制器方法发生在请求管道之外，localizer 读取的是线程 CurrentUICulture。
        WithCulture("zh-CN", () =>
            controller.AddAgency(new AgencyModel { AgencyId = "usa.foo", Label = "Test" }).GetAwaiter().GetResult());

        var errors = Assert.Single(controller.ModelState[""].Errors);
        Assert.Contains("机构标识符必须以", errors.ErrorMessage);
    }
}
