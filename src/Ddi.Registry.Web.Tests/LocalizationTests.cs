using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Ddi.Registry.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Ddi.Registry.Web.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public async Task SetLanguage_ZhCn_SetsCultureCookieAndRedirectsToReturnUrl()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsync(
            "/Language/SetLanguage?culture=zh-CN&returnUrl=/Home/Index", new StringContent(""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/Index", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers, h => h.Key == "Set-Cookie");
    }

    [Fact]
    public async Task SetLanguage_ExternalReturnUrl_RedirectsToHomeNotExternal()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsync(
            "/Language/SetLanguage?culture=en&returnUrl=https://evil.example.com", new StringContent(""));

        // 外部 returnUrl 不安全，回退到首页（默认路由生成 "/"）
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Home_DefaultCulture_IsChinese()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        // Razor HTML-encodes non-ASCII localized output (e.g. 注册表工具 becomes
        // &#x6CE8;&#x518C;&#x8868;&#x5DE5;&#x5177;), so decode before asserting.
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("注册表工具", decoded);  // Nav.RegistryTools 中文词条
    }

    [Fact]
    public async Task Home_QueryCultureEn_IsEnglish()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?culture=en");
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Registry Tools", decoded);
    }
}
