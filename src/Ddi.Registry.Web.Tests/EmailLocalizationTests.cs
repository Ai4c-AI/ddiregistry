using System;
using System.Globalization;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Controllers;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Ddi.Registry.Web.Tests;

public sealed class EmailLocalizationTests
{
    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? Subject { get; private set; }
        public string? Body { get; private set; }
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Subject = subject;
            Body = htmlMessage;
            return Task.CompletedTask;
        }
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
    public void ControllerLocalizer_ResolvesChineseEmailKeys()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var scope = factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<AdminController>>();

        WithCulture("zh-CN", () =>
            Assert.Contains("已批准", localizer["ApprovedEmailSubject"].Value));
    }

    [Fact]
    public async Task SendApprovedEmail_WithChineseAmbientCulture_SendsChineseEmail()
    {
        var sender = new CapturingEmailSender();
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false,
            configureServices: services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });

        using var scope = factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<AdminController>>();
        // 控制器默认未注册为 DI 服务，手动构造；SendApprovedEmail 只依赖 localizer 与 sender。
        var admin = new AdminController(null!, null!, null!, sender, localizer);
        var user = new ApplicationUser { Email = "t@example.com" };

        // 直接调用控制器方法发生在请求管道之外，localizer 读取的是线程 CurrentUICulture，
        // 因此显式设置环境文化，而不是依赖请求 Cookie。
        WithCulture("zh-CN", () =>
            admin.SendApprovedEmail(user, "us.testorg").GetAwaiter().GetResult());

        Assert.Contains("已批准", sender.Subject);
        Assert.Contains("DDI Alliance", sender.Body);
    }
}
