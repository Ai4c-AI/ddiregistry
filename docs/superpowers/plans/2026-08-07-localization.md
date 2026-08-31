# 全球化与本地化（Globalization & Localization）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 DDI Registry Web 应用接入 ASP.NET Core 10 本地化管线，默认简体中文、支持英文，覆盖视图/导航/校验/控制器/邮件/Identity 页面。

**Architecture:** 使用 ASP.NET Core 内置本地化：`AddLocalization(ResourcesPath="Resources")` + `AddViewLocalization(Suffix)` + `AddDataAnnotationsLocalization`（统一指向 `SharedResource`）+ `UseRequestLocalization` 中间件。语言确定优先级：`?culture=` 查询参数 → Cookie → 浏览器 `Accept-Language` → 默认 `zh-CN`。资源用 `.resx` 双语言文件，中性（invariant）资源为英文、`zh-CN` 资源为中文，中文缺词条自动回落英文。导航栏语言下拉框写入文化 Cookie。

**Tech Stack:** .NET 10 / ASP.NET Core MVC + Razor Pages（Identity）、`.resx` 嵌入式资源、xunit.v3（Web.Tests）。

## Global Constraints

- 目标框架 `net10.0`；零新增第三方依赖（仅用 ASP.NET Core 内置本地化 API）。
- 默认请求文化：`zh-CN`；支持文化：`zh-CN`、`en`。
- 中性（invariant）资源 = 英文；`zh-CN` 资源 = 中文；中文缺词条回落英文，绝不显示资源键名。
- 所有 `.resx` 置于 `Ddi.Registry.Web/Resources/` 下；`ResourcesPath = "Resources"`。
- 专有名词（DDI、Agency Registry、Keycloak、DDI Alliance、机构名、域名）不翻译。
- 邮件本地化跟随**发送者当前请求语言**（`IStringLocalizer` 在请求内解析）。
- 数据注解消息统一走 `SharedResource`；标记类 `SharedResource` 命名空间为 `Ddi.Registry.Web`，**必须放在项目根**（`src/Ddi.Registry.Web/SharedResource.cs`），不能放在 `Resources/` 目录或 `...Resources` 命名空间下——否则资源 base name 会变成 `...Resources.Resources.SharedResource`（`Resources` 重复），数据注解本地化整体失效。
- 视图正文本地化用 `@inject IViewLocalizer Localizer` + `Localizer["Key"]`（`_ViewImports` 已 `@using Microsoft.AspNetCore.Mvc.Localization`）；**导航/共享文案**（词条放在 SharedResource）用 `@inject IStringLocalizer<SharedResource> Localizer`。二者**不混用**：`IViewLocalizer` 只查自身视图路径对应的资源，**不会**回落到 SharedResource——把 SharedResource 里的键用 `IViewLocalizer` 读取会渲染出键名本身。
- 每个任务结束需 `dotnet build Ddi.Registry.Web.sln` 通过，且 `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj` 通过，然后提交。

---

## 文件结构总览

| 文件 | 职责 |
| --- | --- |
| `SharedResource.cs`（项目根） | 空标记类，数据注解与共享文案的本地化源 |
| `Resources/SharedResource.resx` / `.zh-CN.resx` | 共享/导航/校验词条（英文 / 中文） |
| `Resources/Views/**/*.resx` / `.zh-CN.resx` | 各 MVC 视图词条 |
| `Resources/Controllers/*.resx` / `.zh-CN.resx` | 各控制器（含邮件）词条 |
| `Resources/Areas/Identity/Pages/Account/*.resx` / `.zh-CN.resx` | Identity 覆盖页词条 |
| `Controllers/LanguageController.cs` | 语言切换动作（写文化 Cookie + 重定向） |
| `Views/Shared/_LanguageSelector.cshtml` | 导航栏语言下拉框 partial |
| `Startup.cs` | 注册本地化服务与 `RequestLocalization` 中间件 |
| `Models/ManageModels.cs` | 数据注解改为 `SharedResource` 资源引用 |

---

### Task 1: 本地化基础设施 + 语言选择器 + 布局/登录导航本地化

**Files:**
- Create: `src/Ddi.Registry.Web/SharedResource.cs`（**项目根**，非 Resources/ 下，原因见全局约束）
- Create: `src/Ddi.Registry.Web/Resources/SharedResource.resx`
- Create: `src/Ddi.Registry.Web/Resources/SharedResource.zh-CN.resx`
- Modify: `src/Ddi.Registry.Web/Startup.cs`（ConfigureServices 与 Configure）
- Create: `src/Ddi.Registry.Web/Controllers/LanguageController.cs`
- Create: `src/Ddi.Registry.Web/Views/Shared/_LanguageSelector.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/_ViewImports.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/Shared/_Layout.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/Shared/_LoginPartial.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/Shared/_CookieConsentPartial.cshtml`
- Test: `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Produces: `POST /Language/SetLanguage(string culture, string returnUrl)`；`IStringLocalizer<SharedResource>` 可用于后续数据注解任务；`RequestLocalizationOptions` 提供 zh-CN/en 与 Cookie/QueryString/AcceptLanguage provider。

- [ ] **Step 1: 写失败测试** `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

```csharp
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Ddi.Registry.Web.Resources;
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
        var client = factory.CreateClient();

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
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/Language/SetLanguage?culture=en&returnUrl=https://evil.example.com", new StringContent(""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString); // 外部 returnUrl 重定向到默认路由生成的 "/"
    }

    [Fact]
    public async Task Home_DefaultCulture_IsChinese()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("注册表工具", html);  // Nav.RegistryTools 中文词条
    }

    [Fact]
    public async Task Home_QueryCultureEn_IsEnglish()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?culture=en");

        Assert.Contains("Registry Tools", html);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: FAIL —— `LanguageController` 不存在 / 渲染内容仍是英文。

- [ ] **Step 3: 创建标记类与资源文件**

`src/Ddi.Registry.Web/SharedResource.cs`（**项目根**）：
```csharp
namespace Ddi.Registry.Web
{
    /// <summary>
    /// Marker class that serves as the shared resource source for data annotations
    /// and shared UI strings (navigation, common validation messages).
    /// </summary>
    public sealed class SharedResource
    {
    }
}
```

> 说明：必须放在项目根（namespace `Ddi.Registry.Web`）。若放在 `Resources/` 下且 namespace 为 `...Resources`，`IStringLocalizer<SharedResource>` 的 base name 会变为 `Ddi.Registry.Web.Resources.Resources.SharedResource`（`Resources` 重复），导致数据注解本地化与共享文案全部失效。

`Resources/SharedResource.resx`（英文基础词条，格式如下，后续任务不断追加 `<data>` 项）：
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="Nav.Develop" xml:space="preserve"><value>Develop</value></data>
  <data name="Nav.RegistryTools" xml:space="preserve"><value>Registry Tools</value></data>
  <data name="Nav.RegistryDeployment" xml:space="preserve"><value>Registry Deployment</value></data>
  <data name="Nav.RegistryGitHub" xml:space="preserve"><value>DDI Registry GitHub</value></data>
  <data name="Nav.YourAgencies" xml:space="preserve"><value>Your Agencies</value></data>
  <data name="Nav.Contact" xml:space="preserve"><value>Contact</value></data>
  <data name="Nav.Administration" xml:space="preserve"><value>Administration</value></data>
  <data name="Nav.Admin" xml:space="preserve"><value>Admin</value></data>
  <data name="Nav.AgencyAdmin" xml:space="preserve"><value>Agency Admin</value></data>
  <data name="Nav.AgencyAdminHelp" xml:space="preserve"><value>Agency Admin Help</value></data>
  <data name="Nav.SuperAdmin" xml:space="preserve"><value>SuperAdmin</value></data>
  <data name="Nav.UserAdmin" xml:space="preserve"><value>User Admin</value></data>
  <data name="Nav.Privacy" xml:space="preserve"><value>Privacy</value></data>
  <data name="Language.Label" xml:space="preserve"><value>Language</value></data>
</root>
```

`Resources/SharedResource.zh-CN.resx`（结构同上，`<data>` 内容）：
```xml
  <data name="Nav.Develop" xml:space="preserve"><value>开发</value></data>
  <data name="Nav.RegistryTools" xml:space="preserve"><value>注册表工具</value></data>
  <data name="Nav.RegistryDeployment" xml:space="preserve"><value>注册表部署</value></data>
  <data name="Nav.RegistryGitHub" xml:space="preserve"><value>DDI 注册表 GitHub</value></data>
  <data name="Nav.YourAgencies" xml:space="preserve"><value>我的机构</value></data>
  <data name="Nav.Contact" xml:space="preserve"><value>联系我们</value></data>
  <data name="Nav.Administration" xml:space="preserve"><value>管理</value></data>
  <data name="Nav.Admin" xml:space="preserve"><value>管理</value></data>
  <data name="Nav.AgencyAdmin" xml:space="preserve"><value>机构管理</value></data>
  <data name="Nav.AgencyAdminHelp" xml:space="preserve"><value>机构管理帮助</value></data>
  <data name="Nav.SuperAdmin" xml:space="preserve"><value>超级管理员</value></data>
  <data name="Nav.UserAdmin" xml:space="preserve"><value>用户管理</value></data>
  <data name="Nav.Privacy" xml:space="preserve"><value>隐私</value></data>
  <data name="Language.Label" xml:space="preserve"><value>语言</value></data>
```
> 注意：`_Layout` / `_LoginPartial` 视图词条放到各自的视图资源文件（`Resources/Views/Shared/_Layout.zh-CN.resx` 等），**不要**放进 SharedResource；SharedResource 只放跨视图共享/校验词条。导航词条因同时被 `_LanguageSelector`（partial）等使用，放 SharedResource 便于复用。

- [ ] **Step 4: 修改 `Startup.cs`**

`ConfigureServices` 中把：
```csharp
            services.AddMvc();
```
改为：
```csharp
            services.AddLocalization(options => options.ResourcesPath = "Resources");

            services.AddMvc()
                .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
                .AddDataAnnotationsLocalization(options =>
                    options.DataAnnotationLocalizerProvider = (type, factory) =>
                        factory.Create(typeof(SharedResource)));
```

`Configure` 中把：
```csharp
            app.UseCookiePolicy();            

            app.UseRouting();
```
改为：
```csharp
            app.UseCookiePolicy();

            var supportedCultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") };
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("zh-CN"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            });

            app.UseRouting();
```

文件顶部新增 using：
```csharp
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Ddi.Registry.Web.Resources;
```

- [ ] **Step 5: 创建 `LanguageController.cs`**

```csharp
using System;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Ddi.Registry.Web.Controllers
{
    public class LanguageController : Controller
    {
        private static readonly string[] SupportedCultures = { "zh-CN", "en" };

        [HttpPost]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            var resolved = culture != null &&
                Array.IndexOf(SupportedCultures, culture, StringComparer.OrdinalIgnoreCase) >= 0
                ? culture
                : "zh-CN";

            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(resolved)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
```

- [ ] **Step 6: 修改 `_ViewImports.cshtml`** —— 追加一行
三行

```
@using Microsoft.AspNetCore.Mvc.Localization
@using Microsoft.Extensions.Localization
@using Ddi.Registry.Web.Resources

- [ ] **Step 7: 创建 `_LanguageSelector.cshtml`**

```html
@using Microsoft.AspNetCore.Http.Features
@using Microsoft.AspNetCore.Localization
@inject IStringLocalizer<SharedResource> Localizer

@{
    var requestCulture = Context.Features.Get<IRequestCultureFeature>();
    var currentCulture = requestCulture?.RequestCulture.Culture.Name ?? "zh-CN";
    var returnUrl = "~" + Context.Request.Path.Value;
}
<form id="language-selector" asp-controller="Language" asp-action="SetLanguage"
      asp-route-returnUrl="@returnUrl" method="post" class="form-inline ml-2">
    <select name="culture" class="form-control form-control-sm" onchange="this.form.submit()" aria-label="@Localizer["Language.Label"]">
        <option value="zh-CN" selected="@(currentCulture.StartsWith("zh"))">中文</option>
        <option value="en" selected="@(currentCulture.StartsWith("en"))">English</option>
    </select>
</form>
```
> 说明：导航/共享键存在 `SharedResource`，因此这里注入 `IStringLocalizer<SharedResource>`（`_ViewImports` 已带 `@using Microsoft.Extensions.Localization` 与 `@using Ddi.Registry.Web.Resources`），**不要**用 `IViewLocalizer`（它查不到 SharedResource 的键）。

- [ ] **Step 8: 修改 `_Layout.cshtml`**

- 文件顶部 `@{` 前插入：`@inject IStringLocalizer<SharedResource> Localizer`（导航键在 SharedResource，用共享本地化器；`_ViewImports` 已带所需 using）
- `navbar-collapse` 内、`<partial name="_LoginPartial" />` 之后追加：`<partial name="_LanguageSelector" />`
- 替换可见英文：
  - `Develop` → `@Localizer["Nav.Develop"]`
  - `Registry Tools` → `@Localizer["Nav.RegistryTools"]`
  - `Registry Deployment` → `@Localizer["Nav.RegistryDeployment"]`
  - `DDI Registry GitHub` → `@Localizer["Nav.RegistryGitHub"]`
  - 页脚 `Privacy` → `@Localizer["Nav.Privacy"]`
- 品牌 `DDI Agency Registry` 与页脚 `DDI Alliance Agency Registry` 保持不译（品牌专有名词）。

- [ ] **Step 9: 修改 `_LoginPartial.cshtml`**

- 顶部追加 `@inject IStringLocalizer<SharedResource> Localizer`（同上，导航键在 SharedResource）
- `Your Agencies` → `@Localizer["Nav.YourAgencies"]`
- `Contact` → `@Localizer["Nav.Contact"]`
- `Administration` → `@Localizer["Nav.Administration"]`
- `Admin`（下拉分隔标题）→ `@Localizer["Nav.Admin"]`
- `Agency Admin` → `@Localizer["Nav.AgencyAdmin"]`
- `Agency Admin Help` → `@Localizer["Nav.AgencyAdminHelp"]`
- `SuperAdmin`（分隔标题）→ `@Localizer["Nav.SuperAdmin"]`
- `User Admin` → `@Localizer["Nav.UserAdmin"]`
- 各 `title="..."` 属性同步替换为 `title="@Localizer[...]"`。

- [ ] **Step 10: 本地化 `_CookieConsentPartial.cshtml`**

`Views/Shared/_CookieConsentPartial.cshtml` 含可见英文（"This site uses cookies."、"Learn More"、"Accept"），在 `_Layout` 中渲染：
- 注入 `@inject IStringLocalizer<SharedResource> Localizer`
- 替换：`This site uses cookies.` → `@Localizer["Cookie.ConsentMessage"]`、`Learn More` → `@Localizer["Cookie.LearnMore"]`、`Accept` → `@Localizer["Cookie.Accept"]`、`aria-label="Close"` 保持
- 在 `SharedResource.resx` / `.zh-CN.resx` 追加：
```xml
  <!-- 英文 -->
  <data name="Cookie.ConsentMessage" xml:space="preserve"><value>This site uses cookies.</value></data>
  <data name="Cookie.LearnMore" xml:space="preserve"><value>Learn More</value></data>
  <data name="Cookie.Accept" xml:space="preserve"><value>Accept</value></data>
```
```xml
  <!-- 中文 -->
  <data name="Cookie.ConsentMessage" xml:space="preserve"><value>本网站使用 Cookie。</value></data>
  <data name="Cookie.LearnMore" xml:space="preserve"><value>了解更多</value></data>
  <data name="Cookie.Accept" xml:space="preserve"><value>接受</value></data>
```

> 说明：`_Layout`/`_LoginPartial`/`_LanguageSelector` 的导航词条全部集中在 `SharedResource`，**无需**为它们单独建视图资源文件；视图资源（`Resources/Views/**`）只服务于正文视图（Task 4–7）。

- [ ] **Step 11: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: PASS（4 个测试全绿）。

- [ ] **Step 12: 全量构建 + 全量测试 + 提交**

Run: `dotnet build Ddi.Registry.Web.sln -c Debug`
Expected: 0 errors。
Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj`
Expected: 现有测试全绿（含 `KeycloakConfigurationTests`，其断言的是 "Keycloak" 专有名词，不受影响）。
```bash
git add src/Ddi.Registry.Web/Resources src/Ddi.Registry.Web/Controllers/LanguageController.cs src/Ddi.Registry.Web/Views src/Ddi.Registry.Web/Startup.cs src/Ddi.Registry.Web.Tests/LocalizationTests.cs
git commit -m "feat(web): add localization infrastructure and language selector"
```

---

### Task 2: 数据注解本地化（ManageModels → SharedResource）

**Files:**
- Modify: `src/Ddi.Registry.Web/Models/ManageModels.cs`
- Modify: `src/Ddi.Registry.Web/Resources/SharedResource.resx` + `.zh-CN.resx`（追加校验词条）
- Test: 追加到 `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Consumes: `SharedResource`（Task 1）、`AddDataAnnotationsLocalization` 的 `SharedResource` provider（Task 1）。
- Produces: `AgencyModel` 等模型的校验/显示消息可在 zh-CN/en 下正确渲染。

- [ ] **Step 1: 写失败测试**（追加到 LocalizationTests.cs）

```csharp
    [Fact]
    public void SharedResource_LocalizesRequiredMessage_ToChinese()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        using var scope = factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();

        // 请求外解析本地化器读的是线程 CurrentUICulture，必须显式设置，否则结果取决于运行机器
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
            Assert.Contains("不能为空", localizer["AgencyNameRequired"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            CultureInfo.CurrentCulture = original;
        }
    }
```
> 说明：无认证环境下 Manage 页面会重定向登录页，因此数据注解词条用 DI 解析 `IStringLocalizer<SharedResource>` 验证；**必须显式设置线程文化**。文件顶部需补 `using System.Globalization;`。视图渲染验证在 Task 6 的 Manage 视图测试中覆盖（会带测试认证）。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: FAIL —— `AgencyNameRequired` 词条不存在（返回键名本身）。

- [ ] **Step 3: 修改 `ManageModels.cs`**

文件顶部加 `using Ddi.Registry.Web.Resources;`。将以下属性/注解改为资源引用（`Description` 保持硬编码，本轮不本地化 Description）：

`AgencyId`：
```csharp
        [RegularExpression(@"[a-zA-Z]{2,3}[\.][a-zA-Z0-9](-?[a-zA-Z0-9]+)*",
            ErrorMessageResourceName = "AgencyNamePattern", ErrorMessageResourceType = typeof(SharedResource))]
        [StringLength(50)]
        [Required(ErrorMessageResourceName = "AgencyNameRequired", ErrorMessageResourceType = typeof(SharedResource))]
        [Display(Name = "AgencyName", ResourceType = typeof(SharedResource))]
        public string AgencyId { get; set; }
```

`Label`：
```csharp
        [Required(ErrorMessageResourceName = "AgencyLabelRequired", ErrorMessageResourceType = typeof(SharedResource))]
        [Display(Name = "AgencyLabel", ResourceType = typeof(SharedResource))]
        public string Label { get; set; }
```

`TechnicalContactId`、`TechnicalContactEmail`、`AdminContactId`、`AdminContactEmail`、`CreatorId`：
```csharp
        [Display(Name = "TechnicalContact", ResourceType = typeof(SharedResource))]
        ...
        [Display(Name = "TechnicalContactEmail", ResourceType = typeof(SharedResource))]
        ...
        [Display(Name = "AdministrativeContact", ResourceType = typeof(SharedResource))]
        ...
        [Display(Name = "AdministrativeContactEmail", ResourceType = typeof(SharedResource))]
        ...
        [Display(Name = "Creator", ResourceType = typeof(SharedResource))]
```

`AssignmentModel.AssignmentId`：
```csharp
        [RegularExpression(@"^([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])(\.([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]{0,61}[a-zA-Z0-9]))*$",
            ErrorMessageResourceName = "SubdomainPattern", ErrorMessageResourceType = typeof(SharedResource))]
        [StringLength(255)]
        [Required]
        public string AssignmentId { get; set; }
```
> `ConceptRegistrationModel` / `RepresentationRegistrationModel` / `VariableRegistrationModel` 的裸 `[Required]`/`[EmailAddress]`/`[StringLength]` **没有**自定义消息。`AddDataAnnotationsLocalization` 会以属性内置英文消息串作为键去 `SharedResource` 查找（例如 `"The {0} field is required."`），**必须**在 Step 4 中为这些内置消息串添加词条，否则 zh-CN 下仍显示英文。

- [ ] **Step 4: 追加 SharedResource 词条**

`SharedResource.resx`（英文）追加：
```xml
  <data name="AgencyName" xml:space="preserve"><value>Agency Name</value></data>
  <data name="AgencyNameRequired" xml:space="preserve"><value>An agency name is required.</value></data>
  <data name="AgencyNamePattern" xml:space="preserve"><value>The agency name should be in the form [country code] dot [name]. For example: us.agencyname</value></data>
  <data name="AgencyLabel" xml:space="preserve"><value>Agency Label</value></data>
  <data name="AgencyLabelRequired" xml:space="preserve"><value>An agency label is required.</value></data>
  <data name="TechnicalContact" xml:space="preserve"><value>Technical Contact</value></data>
  <data name="TechnicalContactEmail" xml:space="preserve"><value>Technical Contact Email</value></data>
  <data name="AdministrativeContact" xml:space="preserve"><value>Administrative Contact</value></data>
  <data name="AdministrativeContactEmail" xml:space="preserve"><value>Administrative Contact Email</value></data>
  <data name="Creator" xml:space="preserve"><value>Creator</value></data>
  <data name="SubdomainPattern" xml:space="preserve"><value>The sub domain must contain letters, numbers, and dots only, and begin with the agency name</value></data>
```

`SharedResource.zh-CN.resx` 追加：
```xml
  <data name="AgencyName" xml:space="preserve"><value>机构名称</value></data>
  <data name="AgencyNameRequired" xml:space="preserve"><value>机构名称不能为空。</value></data>
  <data name="AgencyNamePattern" xml:space="preserve"><value>机构名称格式应为 [国家/地区代码].[名称]，例如 us.agencyname</value></data>
  <data name="AgencyLabel" xml:space="preserve"><value>机构标签</value></data>
  <data name="AgencyLabelRequired" xml:space="preserve"><value>机构标签不能为空。</value></data>
  <data name="TechnicalContact" xml:space="preserve"><value>技术联系人</value></data>
  <data name="TechnicalContactEmail" xml:space="preserve"><value>技术联系人邮箱</value></data>
  <data name="AdministrativeContact" xml:space="preserve"><value>行政联系人</value></data>
  <data name="AdministrativeContactEmail" xml:space="preserve"><value>行政联系人邮箱</value></data>
  <data name="Creator" xml:space="preserve"><value>创建者</value></data>
  <data name="SubdomainPattern" xml:space="preserve"><value>子域名只能包含字母、数字和点，且必须以机构名称开头</value></data>
```

再追加**内置校验消息**词条（键名必须与 DataAnnotations 内置英文消息串**完全一致**，含 `{0}`/`{1}` 占位符，否则查找失败回退英文）：

`SharedResource.resx`（英文）追加：
```xml
  <data name="The {0} field is required." xml:space="preserve"><value>The {0} field is required.</value></data>
  <data name="The {0} field is not a valid e-mail address." xml:space="preserve"><value>The {0} field is not a valid e-mail address.</value></data>
  <data name="The field {0} must be a string with a maximum length of {1}." xml:space="preserve"><value>The field {0} must be a string with a maximum length of {1}.</value></data>
```

`SharedResource.zh-CN.resx` 追加：
```xml
  <data name="The {0} field is required." xml:space="preserve"><value>{0} 字段是必填项。</value></data>
  <data name="The {0} field is not a valid e-mail address." xml:space="preserve"><value>{0} 字段不是有效的电子邮件地址。</value></data>
  <data name="The field {0} must be a string with a maximum length of {1}." xml:space="preserve"><value>字段 {0} 必须是最大长度为 {1} 的字符串。</value></data>
```
> 覆盖范围：本项目实际出现的裸 `[Required]`、`[EmailAddress]`、`[StringLength]`（仅最大值）。若实现时发现其它内置消息（如 `[StringLength]` 同时含最小值），按同规则补齐。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: PASS。

- [ ] **Step 6: 全量构建 + 全量测试 + 提交**

Run: `dotnet build Ddi.Registry.Web.sln -c Debug` → 0 errors；`dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj` → 全绿。
```bash
git add src/Ddi.Registry.Web/Models/ManageModels.cs src/Ddi.Registry.Web/Resources
git commit -m "feat(web): localize data annotation messages via SharedResource"
```

---

### Task 3: 控制器文案 + 审批/邀请邮件本地化

**Files:**
- Modify: `src/Ddi.Registry.Data/AgencyIdValidator.cs`（为验证结果增加稳定 `ErrorCode`，Web 按代码本地化，MCP 仍可使用英文 `Error`）
- Modify: `src/Ddi.Registry.Web/Controllers/HomeController.cs`
- Modify: `src/Ddi.Registry.Web/Controllers/ManageController.cs`
- Modify: `src/Ddi.Registry.Web/Controllers/AdminController.cs`
- Modify: `src/Ddi.Registry.Mcp.Tests/AgencyIdValidatorTests.cs`（覆盖错误码）
- Modify: `src/Ddi.Registry.Web.Tests/WebOidcApplicationFactory.cs`（加可选服务配置钩子，供测试替换 `IEmailSender`）
- Create: `src/Ddi.Registry.Web/Resources/Controllers/HomeController.resx` + `.zh-CN.resx`
- Create: `src/Ddi.Registry.Web/Resources/Controllers/ManageController.resx` + `.zh-CN.resx`
- Create: `src/Ddi.Registry.Web/Resources/Controllers/AdminController.resx` + `.zh-CN.resx`
- Test: `src/Ddi.Registry.Web.Tests/EmailLocalizationTests.cs`（新建）

**Interfaces:**
- Consumes: `SharedResource` 无依赖；`IEmailSender`（现有）；`WebOidcApplicationFactory(bool configureKeycloak, string environmentName = "Testing", Action<IServiceCollection>? configureServices = null)`。
- Produces: `AgencyIdValidationResult(bool Ok, string ErrorCode, string Error)`；`ManageController` 的 8 条 `ModelState` 错误路径及审批/邀请邮件随请求文化本地化。经全文件扫描，`ManageController` 当前没有 `ViewData["Title"]` 或 `TempData`。

- [ ] **Step 1: 给 `WebOidcApplicationFactory` 加可选服务配置钩子**

在 `src/Ddi.Registry.Web.Tests/WebOidcApplicationFactory.cs`：
- 构造函数增加可选参数 `Action<IServiceCollection>? configureServices = null` 并存到字段
- 在 `ConfigureWebHost` 的 `builder.ConfigureTestServices(services => { ... })` 块末尾追加：`configureServices?.Invoke(services);`

```csharp
    private readonly bool _configureKeycloak;
    private readonly string _environmentName;
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Action<IServiceCollection>? _configureServices;

    public WebOidcApplicationFactory(bool configureKeycloak, string environmentName = "Testing",
        Action<IServiceCollection>? configureServices = null)
    {
        _configureKeycloak = configureKeycloak;
        _environmentName = environmentName;
        _configureServices = configureServices;
    }
```
并在 `ConfigureTestServices` 的 lambda 内（`options.UseInMemoryDatabase(_databaseName));` 之后）加：
```csharp
            configureServices?.Invoke(services);
```
> 该钩子同时可供 Task 6 的 Manage 视图测试注入认证等使用。

- [ ] **Step 2: 写失败测试** `src/Ddi.Registry.Web.Tests/EmailLocalizationTests.cs`

```csharp
using System;
using System.Globalization;
using System.Threading.Tasks;
using Ddi.Registry.Web.Controllers;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var admin = scope.ServiceProvider.GetRequiredService<AdminController>();
        var user = new Ddi.Registry.Data.ApplicationUser { Email = "t@example.com" };

        // 直接调用控制器方法发生在请求管道之外，localizer 读取的是线程 CurrentUICulture，
        // 因此显式设置环境文化，而不是依赖请求 Cookie。
        WithCulture("zh-CN", () =>
            admin.SendApprovedEmail(user, "us.testorg").GetAwaiter().GetResult());

        Assert.Contains("已批准", sender.Subject);
        Assert.Contains("DDI Alliance", sender.Body);
    }
}
```
> 注意：`AdminController` 构造函数现已注入 `IStringLocalizer<AdminController>`，DI 可直接解析控制器实例。`IStringLocalizer<T>` 是单例，解析时读取线程 `CurrentUICulture`，所以**必须**用 `WithCulture` 显式设置环境文化（请求内 Cookie/查询参数只影响管道内的 HttpContext 文化，不影响管道外的直接调用）。`admin.SendApprovedEmail` 是 `public` 方法。

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~EmailLocalizationTests"`
Expected: FAIL —— `AdminController` 构造要求新增 localizer 参数导致编译失败 / 无中文键资源。

- [ ] **Step 4: 修改 `AdminController.cs`**

- 顶部加 `using Microsoft.Extensions.Localization;`、`using System.Text.Encodings.Web;`
- 类内加字段并在构造函数注入：
```csharp
        private readonly IStringLocalizer<AdminController> _localizer;

        public AdminController(ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender email,
            IStringLocalizer<AdminController> localizer)
        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _email = email;
            _localizer = localizer;
        }
```
- 本地化邮件方法：
```csharp
        public async Task SendApprovedEmail(ApplicationUser user, string agencyName)
        {
            var bodyHtml = string.Format(_localizer["ApprovedEmailBody"], agencyName);
            var subject = string.Format(_localizer["ApprovedEmailSubject"], agencyName);
            await _email.SendEmailAsync(user.Email, subject, bodyHtml);
        }

        public async Task SendDeniedEmail(ApplicationUser user, string agencyName, string reason)
        {
            var bodyHtml = string.Format(_localizer["DeniedEmailBody"], agencyName, reason);
            var subject = string.Format(_localizer["DeniedEmailSubject"], agencyName);
            await _email.SendEmailAsync(user.Email, subject, bodyHtml);
        }
```
- 经核查，`AdminController` 的 `Index`/`Approve`/`Delete` 等 action 当前**不设置** `ViewData["Title"]`，也**无** `TempData` 提示或 `ModelState.AddModelError` 硬编码文案，因此本任务仅需处理两个邮件方法。若实现时发现任何 `ViewData["Title"]`/`TempData`/`ModelState` 硬编码字符串，一律改为 `_localizer["Key"]` 并追加对应控制器资源词条。

- [ ] **Step 5: 为 `AgencyIdValidator` 增加稳定错误码**

将当前命名元组返回值改为记录类型，同时保留现有 `.Ok`/`.Error` 消费接口：

```csharp
    public sealed record AgencyIdValidationResult(bool Ok, string ErrorCode, string Error);
```

`Validate` 每个失败分支返回稳定代码，英文 `Error` 保留给 MCP：

| ErrorCode | 现有英文 Error |
| --- | --- |
| `AgencyNameRequired` | An agency name is required. |
| `AgencyLabelRequired` | An agency label is required. |
| `AgencyNameTooLong` | The agency name must be 50 characters or fewer. |
| `AgencyNamePattern` | The agency name should be in the form ... |
| `CountryCodeDataUnavailable` | ISO country-code validation data is unavailable. |
| `CountryCodeInvalid` | `{code}` is not a valid country code... |
| `AgencyPrefixInvalid` | The agency id must start with... |

`CountryCodeInvalid` 的本地化消息需要把国家代码作为参数，因此 Web 控制器从 `AgencyId` 的点号前缀提取该值。更新 `AgencyIdValidatorTests`，至少断言空名称返回 `AgencyNameRequired`、非法国家代码返回 `CountryCodeInvalid`、缺少合法前缀返回 `AgencyPrefixInvalid`。

- [ ] **Step 6: 修改 `ManageController.cs`**

- 顶部加 `using Microsoft.Extensions.Localization;`
- 构造函数注入 `IStringLocalizer<ManageController> _localizer`
- 本地化 `SelectOrInviteUser` 中的邀请邮件（约 1065 行）：
```csharp
                    var subject = _localizer["InviteEmailSubject"];
                    var body = string.Format(_localizer["InviteEmailBody"],
                        inviter.Name, inviter.Email, agencyId, HtmlEncoder.Default.Encode(callbackUrl));
                    await _email.SendEmailAsync(email, subject, body);
```
- 本地化 `SendApproverEmail` / `SendConfirmationEmail`：
```csharp
        private async Task SendApproverEmail(ApplicationUser approver, ApplicationUser user, string agencyName)
        {
            var bodyHtml = string.Format(_localizer["ApproverEmailBody"],
                user.Name, user.Email, agencyName);
            var subject = string.Format(_localizer["ApproverEmailSubject"], agencyName);
            await _email.SendEmailAsync(approver.Email, subject, bodyHtml);
        }

        private async Task SendConfirmationEmail(ApplicationUser user, string agencyName)
        {
            var bodyHtml = string.Format(_localizer["ConfirmationEmailBody"], agencyName);
            var subject = string.Format(_localizer["ConfirmationEmailSubject"], agencyName);
            await _email.SendEmailAsync(user.Email, subject, bodyHtml);
        }
```
- 将全部 8 条 `ModelState` 错误路径逐项替换，不留“实现时核对”：

| 当前位置/来源 | 资源键 | 参数 |
| --- | --- | --- |
| AddAssignment: agency prefix | `AssignmentAgencyPrefixRequired` | 无 |
| AddAssignment: duplicate sub-agency | `SubAgencyExists` | `{0}=assignmentName` |
| AddConceptRegistration: duplicate | `ConceptExists` | 无 |
| AddRepresentationRegistration: duplicate | `RepresentationExists` | 无 |
| AddVariableRegistration: missing reference | `VariableReferenceNotFound` | 无 |
| AddVariableRegistration: `RegistrationValidation.ErrorCode` | `CrossAgencyReference` | 无；按 ErrorCode 查资源，禁止直接显示英文 ErrorMessage |
| AddVariableRegistration: duplicate | `VariableExists` | 无 |
| AddAgency: `AgencyIdValidator.ErrorCode` | 与 Step 5 的 ErrorCode 同名 | `CountryCodeInvalid` 传 `{0}=code`；禁止直接显示英文 Error |

示例：
```csharp
ModelState.AddModelError(string.Empty, _localizer["SubAgencyExists", assignmentName]);

var validation = AgencyIdValidator.Validate(addAgencyModel.AgencyId, addAgencyModel.Label);
if (!validation.Ok)
{
    var countryCode = addAgencyModel.AgencyId?.Split('.')[0] ?? string.Empty;
    ModelState.AddModelError(string.Empty,
        validation.ErrorCode == "CountryCodeInvalid"
            ? _localizer[validation.ErrorCode, countryCode]
            : _localizer[validation.ErrorCode]);
}
```

`RegistrationValidation` 已提供稳定 `ErrorCode`，直接 `_localizer[validation.ErrorCode]`。不要把 Web 本地化依赖引入 Data 项目。

- [ ] **Step 7: 创建控制器资源文件**

`Resources/Controllers/AdminController.resx`（英文）：
```xml
  <data name="ApprovedEmailSubject" xml:space="preserve"><value>DDI Registry - Agency Approved: {0}</value></data>
  <data name="ApprovedEmailBody" xml:space="preserve"><value><![CDATA[<p>The following agency identifier has been approved:</p><p>{0}</p><p>Thank you,<br/>The DDI Alliance</p>]]></value></data>
  <data name="DeniedEmailSubject" xml:space="preserve"><value>DDI Registry - Agency Denied: {0}</value></data>
  <data name="DeniedEmailBody" xml:space="preserve"><value><![CDATA[<p>The following request for an agency identifier has been denied:</p><p>{0}</p><p>The reason given was:</p><p>{1}</p><p>Thank you,<br/>The DDI Alliance</p>]]></value></data>
```
> 提示：`<data>` 值含 `<p>` 等 HTML 时用 `<![CDATA[ ... ]]>` 包裹避免 XML 解析问题。

`Resources/Controllers/AdminController.zh-CN.resx`：
```xml
  <data name="ApprovedEmailSubject" xml:space="preserve"><value>DDI 注册表 - 机构已批准：{0}</value></data>
  <data name="ApprovedEmailBody" xml:space="preserve"><value><![CDATA[<p>以下机构标识符已获批准：</p><p>{0}</p><p>谢谢，<br/>DDI Alliance</p>]]></value></data>
  <data name="DeniedEmailSubject" xml:space="preserve"><value>DDI 注册表 - 机构已拒绝：{0}</value></data>
  <data name="DeniedEmailBody" xml:space="preserve"><value><![CDATA[<p>以下机构标识符申请已被拒绝：</p><p>{0}</p><p>给出的原因是：</p><p>{1}</p><p>谢谢，<br/>DDI Alliance</p>]]></value></data>
```

`Resources/Controllers/ManageController.resx`（英文）+ `.zh-CN.resx`（中文）：
```xml
  <!-- 英文 -->
  <data name="InviteEmailSubject" xml:space="preserve"><value>You have been invited to the DDI Registry - Confirm your email</value></data>
  <data name="InviteEmailBody" xml:space="preserve"><value><![CDATA[{0} ({1}) Has invited you to manage the DDI Agency Id {2}. Please confirm your account by <a href='{3}'>clicking here</a>.]]></value></data>
  <data name="ApproverEmailSubject" xml:space="preserve"><value>DDI Registry - Agency Approval Request: {0}</value></data>
  <data name="ApproverEmailBody" xml:space="preserve"><value><![CDATA[<p>{0} {1} has submitted the following request for a new agency identifier:</p><p>{2}</p><p>Please review the agency at <a href="https://registry.ddialliance.org/Admin">https://registry.ddialliance.org/Admin</a>.</p><p>Thank you,<br/>The DDI Alliance</p>]]></value></data>
  <data name="ConfirmationEmailSubject" xml:space="preserve"><value>DDI Registry - Agency Request: {0}</value></data>
  <data name="ConfirmationEmailBody" xml:space="preserve"><value><![CDATA[<p>You submitted the following request for a new agency identifier:</p><p>{0}</p><p>You will receive a separate confirmation when your request has been processed.</p><p>Thank you,<br/>The DDI Alliance</p>]]></value></data>
  <data name="AgencyIdExists" xml:space="preserve"><value>The agency id already exists, please try again</value></data>
    <data name="AssignmentAgencyPrefixRequired" xml:space="preserve"><value>The agency must start with the agency id</value></data>
    <data name="SubAgencyExists" xml:space="preserve"><value>Sub agency already exists: {0}</value></data>
    <data name="ConceptExists" xml:space="preserve"><value>The concept already exists, please try again</value></data>
    <data name="RepresentationExists" xml:space="preserve"><value>The representation already exists, please try again</value></data>
    <data name="VariableReferenceNotFound" xml:space="preserve"><value>The concept or representation reference could not be found.</value></data>
    <data name="CrossAgencyReference" xml:space="preserve"><value>Variable references must remain within the same agency.</value></data>
    <data name="VariableExists" xml:space="preserve"><value>The variable already exists, please try again</value></data>
```
```xml
  <!-- 中文 -->
  <data name="InviteEmailSubject" xml:space="preserve"><value>您已被邀请加入 DDI 注册表 - 确认您的邮箱</value></data>
  <data name="InviteEmailBody" xml:space="preserve"><value><![CDATA[{0}（{1}）邀请您管理 DDI 机构标识符 {2}。请通过 <a href='{3}'>点击此处</a> 确认您的账户。]]></value></data>
  <data name="ApproverEmailSubject" xml:space="preserve"><value>DDI 注册表 - 机构批准申请：{0}</value></data>
  <data name="ApproverEmailBody" xml:space="preserve"><value><![CDATA[<p>{0} {1} 提交了以下新机构标识符申请：</p><p>{2}</p><p>请前往 <a href="https://registry.ddialliance.org/Admin">https://registry.ddialliance.org/Admin</a> 审核该机构。</p><p>谢谢，<br/>DDI Alliance</p>]]></value></data>
  <data name="ConfirmationEmailSubject" xml:space="preserve"><value>DDI 注册表 - 机构申请：{0}</value></data>
  <data name="ConfirmationEmailBody" xml:space="preserve"><value><![CDATA[<p>您提交了以下新机构标识符申请：</p><p>{0}</p><p>您的申请处理完成后将收到单独确认。</p><p>谢谢，<br/>DDI Alliance</p>]]></value></data>
  <data name="AgencyIdExists" xml:space="preserve"><value>该机构标识符已存在，请重试</value></data>
    <data name="AssignmentAgencyPrefixRequired" xml:space="preserve"><value>子机构标识符必须以机构标识符开头</value></data>
    <data name="SubAgencyExists" xml:space="preserve"><value>子机构已存在：{0}</value></data>
    <data name="ConceptExists" xml:space="preserve"><value>该概念已存在，请重试</value></data>
    <data name="RepresentationExists" xml:space="preserve"><value>该表示已存在，请重试</value></data>
    <data name="VariableReferenceNotFound" xml:space="preserve"><value>找不到引用的概念或表示。</value></data>
    <data name="CrossAgencyReference" xml:space="preserve"><value>变量引用必须属于同一机构。</value></data>
    <data name="VariableExists" xml:space="preserve"><value>该变量已存在，请重试</value></data>
```

同一 `ManageController` 资源文件还必须包含 Step 5 的 7 个 AgencyIdValidator 错误码对应中英文词条；`CountryCodeInvalid` 使用 `{0}` 占位符。英文值保持 Data 层当前文案，中文分别为“机构名称不能为空”“机构标签不能为空”“机构名称不能超过 50 个字符”“机构名称格式应为…”“ISO 国家/地区代码验证数据不可用”“{0} 不是有效的国家/地区代码…”“机构标识符必须以…开头”。

- [ ] **Step 8: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~EmailLocalizationTests"`
Expected: PASS。

- [ ] **Step 9: 全量构建 + 全量测试 + 提交**

```bash
git add src/Ddi.Registry.Data/AgencyIdValidator.cs src/Ddi.Registry.Mcp.Tests/AgencyIdValidatorTests.cs src/Ddi.Registry.Web/Controllers src/Ddi.Registry.Web/Resources/Controllers src/Ddi.Registry.Web.Tests/EmailLocalizationTests.cs
git commit -m "feat(web): localize controller strings and approval/invite emails"
```

---

### Task 4: Home + Help 视图本地化

**Files:**
- Modify: `src/Ddi.Registry.Web/Views/Home/Index.cshtml`、`Privacy.cshtml`、`Tools.cshtml`、`RegistrySource.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/Help/Index.cshtml`、`Administrator.cshtml`
- Modify: `src/Ddi.Registry.Web/Views/Shared/SearchControl.cshtml`（首页搜索框，含 "Search the Registry" / placeholder / "Search"）
- Modify: `src/Ddi.Registry.Web/Views/Shared/Error.cshtml`
- Create: `Resources/Views/Home/*.resx` + `.zh-CN.resx`（Index/Privacy/Tools/RegistrySource）
- Create: `Resources/Views/Help/*.resx` + `.zh-CN.resx`
- Create: `Resources/Views/Shared/SearchControl.resx` + `.zh-CN.resx`、`Error.resx` + `.zh-CN.resx`
- Test: 追加到 `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Consumes: `IViewLocalizer`（_ViewImports 已启用，Task 1）。
- Produces: 各视图随请求文化渲染。

- [ ] **Step 1: 写失败测试**

```csharp
    [Fact]
    public async Task Home_Default_ShowsChineseWelcome()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("欢迎使用 DDI 注册表", html);
    }

    [Fact]
    public async Task Help_QueryCultureEn_IsEnglish()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Help?culture=en");
        Assert.Contains("Help", html);
    }
```
> 断言值以实际中文词条为准，Step 3 里同步调整断言为最终中文文案。

- [ ] **Step 2: 运行测试确认失败**

Expected: FAIL（当前纯英文）。

- [ ] **Step 3: 迁移 Home 视图**

对每个视图（以 `Index.cshtml` 为例）：
1. 顶部 `@{ ... }` 前插入 `@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer Localizer`
2. `ViewData["Title"] = "Home Page"` 改为 `ViewData["Title"] = Localizer["Title"].Value;`（`Localizer["key"].Value` 返回 string；若 `ViewData["Title"]` 直接赋值 `Localizer["key"]` 亦可隐式转换，用 `.Value` 更稳妥）
3. 把每处可见英文文本包成 `@Localizer["Key"]`
4. 在 `Resources/Views/Home/Index.resx` 建英文词条（英文 = 原文），`Index.zh-CN.resx` 建中文词条

`Index.cshtml` 中文词条示例：
```xml
  <data name="Title" xml:space="preserve"><value>欢迎使用 DDI 注册表</value></data>
  <data name="WelcomeHeading" xml:space="preserve"><value>欢迎使用 DDI 注册表</value></data>
  <data name="About" xml:space="preserve"><value>关于</value></data>
  <data name="AboutBody1" xml:space="preserve"><value><![CDATA[<b>DDI 机构注册表</b> 是一个面向元数据生产机构的<em>免费</em>全球唯一标识符系统的组成部分。注册表系统提供机构标识符，该标识符既是全球唯一 ID 中的命名空间，也是分布式服务解析的指针。]]></value></data>
  <data name="RequestAgency" xml:space="preserve"><value>申请 DDI 机构 ID</value></data>
```
> 含 HTML 的段落用 CDATA 包裹；键名用语义化驼峰命名（`Title`、`AboutBody1` 等）。

- [ ] **Step 4: 迁移 Help 视图与共享 partial**

`Help/Index.cshtml`、`Help/Administrator.cshtml` 同 Step 3 模式；建 `Resources/Views/Help/Index.resx`/`.zh-CN.resx`、`Administrator.resx`/`.zh-CN.resx`。

`Views/Shared/SearchControl.cshtml`（首页搜索框）与 `Views/Shared/Error.cshtml`（异常页）同样注入 `IViewLocalizer` 并本地化，建 `Resources/Views/Shared/SearchControl.resx`/`.zh-CN.resx`、`Error.resx`/`.zh-CN.resx`。SearchControl 词条示例：`SearchTheRegistry`（Search the Registry / 检索注册表）、`SearchPlaceholder`（Enter a DDI URN or agency identifier / 输入 DDI URN 或机构标识符）、`SearchButton`（Search / 搜索）。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: PASS。

- [ ] **Step 6: 全量构建 + 全量测试 + 提交**

```bash
git add src/Ddi.Registry.Web/Views/Home src/Ddi.Registry.Web/Views/Help src/Ddi.Registry.Web/Resources/Views src/Ddi.Registry.Web.Tests/LocalizationTests.cs
git commit -m "feat(web): localize Home and Help views"
```

---

### Task 5: Agency + Admin 视图本地化

**Files:**
- Modify: `src/Ddi.Registry.Web/Views/Agency/*`（AgencyList、Index、ListDelegations、ListHttpResolvers、ListServices、Resolver、UnknownAgency，共 7 个）
- Modify: `src/Ddi.Registry.Web/Views/Admin/*`（Index、Delete、EditMember、ShowMembers，共 4 个）
- Create: `Resources/Views/Agency/*.resx` + `.zh-CN.resx`（7 个视图）
- Create: `Resources/Views/Admin/*.resx` + `.zh-CN.resx`（4 个视图）
- Test: 追加到 `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Consumes: `IViewLocalizer`（Task 1）。
- Produces: Agency/Admin 视图随请求文化渲染。

- [ ] **Step 1: 写失败测试**（`AgencyController.Index` 匿名可访问，已核实；`/Agency` 无参数渲染 `AgencyList`，空库时仍渲染 "Agency Search Results" 标题）

```csharp
    [Fact]
    public async Task Agency_DefaultCulture_IsChinese()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Agency");
        Assert.Contains("机构检索结果", html); // Agency Search Results 的中文词条
    }

    [Fact]
    public async Task Agency_QueryCultureEn_IsEnglish()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Agency?culture=en");
        Assert.Contains("Agency Search Results", html);
    }
```
> 计划预设 "Agency Search Results" 中文词条为 "机构检索结果"，实现时以最终 `AgencyList.resx` 词条为准；`/Agency` 为空库渲染 "No Results Found" 分支不影响标题断言。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~LocalizationTests"`
Expected: FAIL（`Agency` 视图当前为英文）。

- [ ] **Step 3: 迁移 Agency 视图**

对每个视图按 Task 4 Step 3 模式处理：注入 `IViewLocalizer`、替换可见英文、建英文/中文视图资源文件。`UnknownAgency.cshtml`、`Resolver.cshtml` 等含提示文案也一并本地化。

- [ ] **Step 4: 迁移 Admin 视图**

同模式处理 `Admin/Index.cshtml`、`Delete.cshtml`、`EditMember.cshtml`、`ShowMembers.cshtml`。

- [ ] **Step 5: 运行测试确认通过 + 全量构建测试 + 提交**

```bash
git add src/Ddi.Registry.Web/Views/Agency src/Ddi.Registry.Web/Views/Admin src/Ddi.Registry.Web/Resources/Views src/Ddi.Registry.Web.Tests/LocalizationTests.cs
git commit -m "feat(web): localize Agency and Admin views"
```

---

### Task 6: Manage 区域视图本地化（21 个视图）

**Files:**
- Modify: `src/Ddi.Registry.Web/Views/Manage/*`（AddAgency、AddAssignment、AddDelegation、AddHttpResolver、AddService、DeleteAssignment、DeleteDelegation、DeleteHttpResolver、DeleteService、EditAgency、EditAssignment、EditDelegation、EditHttpResolver、EditService、Index、ListDelegations、ListHttpResolvers、ListServices、ViewAgency、ViewPerson，共 20 个）
- Create: `Resources/Views/Manage/*.resx` + `.zh-CN.resx`
- Test: 追加到 `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Consumes: `IViewLocalizer`（Task 1）；`IStringLocalizer<ManageController>`（Task 3，用于标题/提示）。
- Produces: Manage 区域视图随请求文化渲染。

- [ ] **Step 1: 写失败测试**（用现有测试认证机制）

参考 `src/Ddi.Registry.Web.Tests/ExternalLoginAccountLinkerTests.cs` / `TripleRegistryManageTests.cs` 中已用的认证方式（`WebOidcApplicationFactory` + 测试认证 handler），请求 `/Manage` 并断言中文文案。实现时复用仓库既有认证测试写法；断言值以最终中文词条为准。

- [ ] **Step 2: 运行测试确认失败**

Expected: FAIL（当前英文）。

- [ ] **Step 3: 迁移 Manage 视图**

按 Task 4 Step 3 模式逐个处理 20 个视图；`ViewAgency`/`EditAgency` 的字段标签优先复用 Task 2 的 `SharedResource` 显示名键（`AgencyName`、`AgencyLabel`、`TechnicalContact` 等），避免重复。
- 同时处理共享显示模板 `Views/Shared/AgencyControl.cshtml`、`PersonControl.cshtml`：若含可见英文列标题/标签（实现时先检查），同样注入 `IViewLocalizer` 并建 `Resources/Views/Shared/AgencyControl.resx`/`PersonControl.resx`（+.zh-CN）。

- [ ] **Step 4: 运行测试确认通过 + 全量构建测试 + 提交**

```bash
git add src/Ddi.Registry.Web/Views/Manage src/Ddi.Registry.Web/Resources/Views/Manage src/Ddi.Registry.Web.Tests/LocalizationTests.cs
git commit -m "feat(web): localize Manage views"
```

---

### Task 7: Identity 页面本地化

**Files:**
- Modify: `src/Ddi.Registry.Web/Areas/Identity/Pages/_ViewImports.cshtml`（追加 `@using Microsoft.AspNetCore.Mvc.Localization`）
- Modify: `src/Ddi.Registry.Web/Areas/Identity/Pages/Account/*.cshtml`（Login、Register、Logout、ConfirmEmail、ForgotPassword、ForgotPasswordConfirmation、Lockout、ExternalLogin、LoginWith2fa、LoginWithRecoveryCode、ResetPassword、ResetPasswordConfirmation、AccessDenied，共 13 个）
- Create: `Resources/Areas/Identity/Pages/Account/*.resx` + `.zh-CN.resx`
- Test: 追加到 `src/Ddi.Registry.Web.Tests/LocalizationTests.cs`

**Interfaces:**
- Consumes: `AddMvc().AddViewLocalization(Suffix)` 已覆盖 Razor Pages（Task 1）。
- Produces: Identity 覆盖页随请求文化渲染。

- [ ] **Step 1: 写失败测试**

```csharp
    [Fact]
    public async Task Login_DefaultCulture_IsChinese()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Identity/Account/Login");
        Assert.Contains("登录", html);
    }

    [Fact]
    public async Task Login_QueryCultureEn_IsEnglish()
    {
        using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Identity/Account/Login?culture=en");
        Assert.Contains("Log in", html);
    }
```

- [ ] **Step 2: 运行测试确认失败**

Expected: FAIL（当前英文）。

- [ ] **Step 3: 迁移 Identity 页面**

- `Areas/Identity/Pages/_ViewImports.cshtml` 追加 `@using Microsoft.AspNetCore.Mvc.Localization`
- 每个页面注入 `@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer Localizer` 并替换可见英文为 `@Localizer["Key"]`
- 每个页面建 `Resources/Areas/Identity/Pages/Account/<Page>.resx`（英文）与 `.zh-CN.resx`（中文）
- 注意：Razor Pages 视图本地化资源名按页面相对路径映射为 `{RootNamespace}.{ResourcesPath}.Areas.Identity.Pages.Account.<Page>`；若实际解析不匹配（运行时资源键名回显），调整为在 `_ViewImports` 的局部资源命名上核对（实现时以运行结果校准，规则不变：中文缺词条回落英文，不显示键名）。

- [ ] **Step 4: 运行测试确认通过 + 全量构建测试 + 提交**

```bash
git add src/Ddi.Registry.Web/Areas/Identity/Pages src/Ddi.Registry.Web/Resources/Areas src/Ddi.Registry.Web.Tests/LocalizationTests.cs
git commit -m "feat(web): localize Identity account pages"
```

---

### Task 8: 收尾验证

**Files:**
- 无新增；全量验证。

- [ ] **Step 1: 全量构建 + 全量测试**

Run: `dotnet build Ddi.Registry.Web.sln -c Debug` → 0 errors
Run: `dotnet test Ddi.Registry.Web.sln -c Debug` → 全部通过（Data/Web/Mcp；MCP 的 Docker 测试可跳过）

运行硬编码文案扫描并逐项清零（允许注释、日志、API 协议文本、专有名词；所有面向用户的 MVC/Identity 文案必须进入资源）：

```powershell
rg -n 'ViewData\["Title"\]|TempData\[|ModelState\.AddModelError|SendEmailAsync' src/Ddi.Registry.Web/Controllers src/Ddi.Registry.Web/Areas/Identity
rg -n '>[^<@]*[A-Za-z][^<]*<|placeholder="[A-Za-z]|title="[A-Za-z]|aria-label="[A-Za-z]' src/Ddi.Registry.Web/Views src/Ddi.Registry.Web/Areas/Identity/Pages --glob '*.cshtml'
```

预期：`ManageController` 无未本地化的 `ModelState.AddModelError` 或邮件文本；视图扫描命中项均属于专有名词、不可见脚本/属性值或已记录的明确例外。把任何新增用户可见命中补入对应 `.resx` 后再继续。

- [ ] **Step 2: 人工验证清单**

1. 启动 Web，默认显示中文首页与中文导航。
2. 导航栏下拉切换 English → 界面变英文；刷新后仍为英文（Cookie 持久化）。
3. URL 加 `?culture=zh-CN` 覆盖 Cookie 生效。
4. 浏览器 Accept-Language 为 en 时默认英文。
5. 登录/注册页中英文正确；逐页检查 Manage 的 Add/Edit/Delete/List/View 页面，提交非法数据时确认 8 条控制器错误路径及数据注解均显示当前语言。
6. 管理员在中文界面批准机构 → 邮件主题/正文为中文。

- [ ] **Step 3: 提交任何遗留改动**

```bash
git add -A
git commit -m "chore(web): finalize localization"
```
