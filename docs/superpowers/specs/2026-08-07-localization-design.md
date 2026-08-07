# 全球化与本地化（Globalization & Localization）设计

- Date: 2026-08-07
- Status: Approved design, ready for implementation planning
- Scope: 为 DDI Registry Web 应用（`Ddi.Registry.Web`）接入 ASP.NET Core 10 本地化基础设施，默认语言简体中文，支持英文，覆盖视图、导航、表单校验、控制器文案、审批/邀请邮件与 Identity 登录/注册页。

## Goals

- 默认以**简体中文（zh-CN）**展示界面，英文（en）作为第二语言。
- 用户可通过**导航栏语言下拉框（Cookie 持久化）**、**URL 查询参数（`?culture=`）**、**浏览器 `Accept-Language` 自动检测**三种方式切换/确定语言。
- 覆盖范围：MVC 视图 + 布局导航、表单校验与数据注解、控制器文案 + 邮件模板、Identity 登录/注册页。
- 审批/邀请邮件跟随**发送者当前请求语言**（管理员界面语言）。
- 零新增第三方依赖，全部使用 ASP.NET Core 内置本地化能力。
- 保持现有测试套件通过。

## Non-goals

- 不本地化业务数据（机构名、机构标签、域名等用户录入内容）。
- 不本地化 MCP 服务（`Ddi.Registry.Mcp` 的工具描述、错误消息），本次仅 Web。
- 不引入第三方本地化/资源管理库。
- 不为用户持久化"语言偏好"字段（邮件跟随发送者当前语言即可，无需存库）。
- 不翻译专有名词（Keycloak、DDI、Agency Registry、DDI Alliance 等品牌/机构名）。

## 决策摘要

| 项 | 决定 |
| --- | --- |
| 语言 | 默认 `zh-CN`；支持 `zh-CN`、`en` |
| 兜底策略 | 中性（invariant）资源 = 英文，中文缺词条自动回落英文 |
| 语言确定优先级 | URL 查询参数 → Cookie → 浏览器 Accept-Language → 默认 zh-CN |
| 范围 | MVC 视图+导航、校验/数据注解、控制器+邮件、Identity 页 |
| 邮件语言 | 跟随发送者当前请求语言 |
| 资源组织 | `.resx` 双语言文件；`SharedResource` 统一管理共享/校验词条 |

## 基础设施（Startup.cs）

### 服务注册（ConfigureServices）

```csharp
services.AddLocalization(options => options.ResourcesPath = "Resources");

// 现有 services.AddMvc() 改为：
services.AddMvc()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(SharedResource)));
```

- `AddMvc()` 同时覆盖 MVC 控制器与 Identity 的 Razor Pages（保留现有页面支持）。
- `AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)` 启用按文化后缀查找视图资源的视图本地化。
- `AddDataAnnotationsLocalization` 将数据注解消息统一路由到 `SharedResource`。

### 中间件（Configure）

放在 `UseCookiePolicy()` 之后、`UseRouting()` 之前：

```csharp
var supportedCultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("zh-CN"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    // 默认 provider 顺序：QueryStringRequestCultureProvider →
    // CookieRequestCultureProvider → AcceptLanguageHeaderRequestCultureProvider
});
```

- 默认请求文化 `zh-CN`：无任何语言信号的首访用户看到中文。
- 浏览器 `Accept-Language` 为英文时自动匹配 `en`；其他未支持语言回落到默认 `zh-CN`。

## 资源文件组织

项目为 SDK 风格，`**/*.resx` 自动编译为嵌入式资源。统一放置于 `Ddi.Registry.Web/Resources/`：

```
Ddi.Registry.Web/
├── SharedResource.cs                     # 空标记类（**项目根**），namespace: Ddi.Registry.Web
├── Resources/
│   ├── SharedResource.resx               # 中性（英文基础文案）
│   ├── SharedResource.zh-CN.resx         # 中文
│   ├── Controllers/
│   │   ├── HomeController.resx / .zh-CN.resx
│   │   ├── ManageController.resx / .zh-CN.resx
│   │   ├── AdminController.resx / .zh-CN.resx
│   │   └── (其余控制器按需)
│   ├── Views/
│   │   ├── Shared/_Layout.resx / .zh-CN.resx    # 导航、页脚等公共文案
│   │   ├── Shared/_LoginPartial.resx / .zh-CN.resx
│   │   └── <Area/Controller>/<View>.resx / .zh-CN.resx
│   └── Areas/Identity/Pages/Account/
│       ├── Login.resx / .zh-CN.resx             # 覆盖后的 Identity 页
│       └── (按需的其余页面)
```

**关键约定**：

- **共享文案**（导航、"提交"/"取消"、通用校验默认消息等）放 `SharedResource`——同时是 `AddDataAnnotationsLocalization` 指向的唯一资源，一处管理所有校验消息。
- **控制器文案**（`ViewData["Title"]`、`TempData` 提示、邮件主题/正文）用 `IStringLocalizer<TController>`，映射到 `Resources/Controllers/<Controller>.zh-CN.resx`。
- **MVC 视图文案**用 `@inject IViewLocalizer Localizer`，映射到 `Resources/Views/<Controller>/<View>.zh-CN.resx`。
- **Identity 覆盖页文案**用 `IViewLocalizer`，映射到 `Resources/Areas/Identity/Pages/Account/<Page>.zh-CN.resx`（具体资源路径在实现时以实际解析结果为准，遵循 `{RootNamespace}.{ResourcesPath}.{相对路径}` 规则）。
- **命名规则**：`.resx` = 英文基础词条，`.zh-CN.resx` = 中文词条；中文缺词条自动回落英文。
- **标记类** `SharedResource.cs` 必须放在**项目根**（namespace `Ddi.Registry.Web`），不能放在 `Resources/` 目录或 `...Resources` 命名空间下。若放在 `Resources/` 下，`IStringLocalizer<SharedResource>` 的 base name 会变成 `Ddi.Registry.Web.Resources.Resources.SharedResource`（`Resources` 重复），导致 `factory.Create(typeof(SharedResource))` 与资源名不一致、数据注解本地化整体失效。

## 各表面迁移

### 3.1 MVC 视图 + 布局导航

- `Views/_ViewImports.cshtml` 增加 `@using Microsoft.AspNetCore.Mvc.Localization`。
- `_Layout.cshtml`、`_LoginPartial.cshtml`、`_CookieConsentPartial.cshtml` 及 `Home`/`Manage`/`Admin`/`Agency`/`Help`/`Resolver` 各视图：注入 `@inject IViewLocalizer Localizer`，将硬编码英文替换为 `Localizer["key"]`。
- 长段落文案（如首页 About 说明、申请机构步骤）整体作为资源词条值。

### 3.2 控制器文案 + 邮件模板

- `HomeController`、`ManageController`、`AdminController` 等注入 `IStringLocalizer<TController>`，替换 `ViewData["Title"]`、`TempData` 提示。
- `ManageController`/`AdminController` 中的审批/邀请邮件（含 `ManageController.cs` 约 1065 行的邀请邮件）主题与 HTML 正文用本地化器构建，**跟随发送者当前请求语言**。
- 邮件 `htmlMessage` 为拼接的 HTML 字符串，本地化词条直接嵌入。

### 3.3 表单校验与数据注解（ManageModels.cs 等）

将硬编码字符串改为资源引用：

```csharp
[Display(Name = "AgencyName", ResourceType = typeof(SharedResource))]
[Required(ErrorMessageResourceName = "AgencyNameRequired", ErrorMessageResourceType = typeof(SharedResource))]
[StringLength(50, ErrorMessageResourceName = "AgencyNameTooLong", ErrorMessageResourceType = typeof(SharedResource))]
```

- 涉及 `[Display]`、`[Required]`、`[StringLength]`、`[RegularExpression]` 等所有带自定义 `ErrorMessage`/`Name` 的属性。
- 词条统一进 `SharedResource.resx` / `.zh-CN.resx`。

### 3.4 Identity 登录/注册页

- Identity UI 来自 Razor Class Library（英文且不内置本地化），需**覆盖面向用户的页面**到 `Areas/Identity/Pages/Account/`（Login、Register、Logout、ConfirmEmail、ForgotPassword、ResetPassword 等），每个覆盖页注入 `@inject IViewLocalizer Localizer` 并本地化。
- 控制覆盖范围：只覆盖真正面向用户、含可见文案的页面；纯内部错误页/校验 partial 视需要处理。
- 每个覆盖页对应 `Resources/Areas/Identity/Pages/Account/<Page>.zh-CN.resx`。

## 语言选择器 UI 与行为

- 在 `_Layout.cshtml` 导航栏新增 `_LanguageSelector.cshtml` partial（语言下拉框，选项：简体中文 / English）。
- 提供 `SetLanguage(string culture, string returnUrl)` 动作（放 `HomeController` 或独立 `LanguageController`）：

```csharp
public IActionResult SetLanguage(string culture, string returnUrl)
{
    Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });
    return LocalRedirect(returnUrl);
}
```

- 下拉框切换通过小表单 POST 到 `SetLanguage`，写入文化 Cookie 后 `LocalRedirect` 回当前页；URL 上的 `?culture=` 始终有效。
- **语言优先级**：`?culture=` > Cookie > `Accept-Language` > 默认 `zh-CN`。

## 错误处理

- 未支持的 culture 值（非法/未列出的语言）由 `RequestLocalizationOptions` 回落机制处理，不抛异常。
- `LocalRedirect` 使用 `LocalRedirect`（非 `Redirect`）防止开放重定向；`returnUrl` 为空时回落首页。
- 中文词条缺失时自动回落到英文基础资源，不显示资源键名。

## 测试策略

### 新增（Ddi.Registry.Web.Tests）

- `SetLanguage` 动作写入文化 Cookie 并正确重定向（含 `returnUrl` 为空、外域 URL 被拒）。
- 请求带 `?culture=en` 渲染英文视图；带 `zh-CN` 或无参数（默认）渲染中文视图。
- `SharedResource` 数据注解本地化生效；中文缺词条回落英文。
- Identity 覆盖页在 zh-CN / en 下渲染正确。

### 现有测试影响

已核查：仅 `KeycloakConfigurationTests` 断言渲染 HTML 中的 `"Keycloak"`（专有名词，不翻译，不受影响）；其余断言为 action 名/模型属性，安全。默认语言改为 `zh-CN` 后，如需测试稳定可在请求上显式带 `?culture=en` 或设置文化 Cookie。

### 人工验证

启动后切换下拉框，检查导航、首页、Manage 表单校验、登录/注册页、审批/邀请邮件的中英文表现；检查浏览器语言分别为中文/英文/其他时的默认落点。

## 部署注意

- `.resx` 由 SDK 自动嵌入，中文资源随程序集一起发布，Docker 构建无需额外处理。
- 无配置项变更需求；支持语言与默认语言在 `Startup.cs` 中硬编码（如需可后续下沉到 `appsettings.json`）。
