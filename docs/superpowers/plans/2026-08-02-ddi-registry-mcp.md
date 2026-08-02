# DDI 注册表 MCP Server 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 DDI Agency Registry 的查询与受限提交能力，封装为经 OAuth 2.0 保护的 Streamable HTTP MCP 工具（resolve_urn、list_agencies、get_services、request_agency）。

**Architecture:** 新建 `src/Ddi.Registry.Mcp`（ASP.NET Core + `ModelContextProtocol` 2.0.0 AspNetCore 集成，Streamable HTTP 传输，OAuth 受保护资源），以项目引用方式复用 `Ddi.Registry.Data` 与 `ApplicationDbContext`，连接与 Web 应用相同的 PostgreSQL 数据库。先做共享重构（移动 `DdiUrn` 并修正 scheme 判断、抽取 `HttpResolver.ResolveUrl`、抽取 `AgencyIdValidator` 到 Data 并引入 NISOCountries，ISO 数据以 Data 的**单一内容文件**复制到应用输出目录），保证 Web 与 MCP 单一真相源。MCP 不运行 EF 迁移，并配置 ForwardedHeaders 以适配 AWS ALB。

**Tech Stack:** .NET 10 / ASP.NET Core、Entity Framework Core 10.0.2 + Npgsql 10.0.0、NISOCountries.Core 1.2.0 + NISOCountries.Ripe 1.2.0、ModelContextProtocol + ModelContextProtocol.AspNetCore 2.0.0、Microsoft.AspNetCore.Authentication.JwtBearer 10.0.2、xUnit + Microsoft.AspNetCore.Mvc.Testing + ModelContextProtocol（客户端）用于集成测试。

## Global Constraints

- 目标框架统一为 **net10.0**。
- EF Core / Npgsql：`Microsoft.EntityFrameworkCore 10.0.2`、`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0`、`Npgsql 10.0.1`。
- MCP 包固定 **2.0.0**；实现 Task 2 时 `dotnet add package --version 2.0.0` 还原到 `~/.nuget` 后再接线。
    - 服务器注册：**`AddMcpServer()` + `WithHttpTransport(options => options.Stateless = true)`（Streamable HTTP，legacy SSE 默认关闭）+ `WithTools<RegistryTools>()`**。无状态部署适合 ALB 多实例；本期不支持 legacy SSE、服务端主动请求、sampling、elicitation 或 roots。
    - **认证接线（关键）**：`AddMcp` 在 2.0.0 是 **`AuthenticationBuilder` 扩展**。Authority 已配置时，`DefaultAuthenticateScheme` 设为 `Bearer`、`DefaultChallengeScheme` 设为 `McpAuth`，再组合 `.AddJwtBearer("Bearer", …).AddMcp(o => { … })`。Bearer 只认证，`McpAuth` 只负责 **401 challenge** 与受保护资源 metadata（默认路径 `/.well-known/oauth-protected-resource/mcp`）。**不要**写成 `builder.Services.AddMcp().WithOAuthAuthorization(...)`（不存在），也不要把授权策略的 scheme 固定为 `Bearer`。
    - `ResourceMetadata` 描述 MCP 受保护资源本身，`AuthorizationServers` 填外部 IdP 的 issuer/base URL，并发布 `ddi.registry.read`、`ddi.registry.write` 为 supported scopes；`ResourceMetadataUri` 只能指向该受保护资源 metadata。本期保留 SDK 默认 metadata URI，不能填 IdP 的 OpenID configuration。
    - Authority 和 Audience 在非 `Development` 环境均为必填；缺失时启动失败。仅 `Development` 可在 Authority 缺失时匿名运行。
    - Authority 已配置时，只对 `/mcp` endpoint builder 施加不指定 scheme 的 `RequireAuthenticatedUser()` 策略；无令牌 401 必须由 `McpAuth` 发出。不得用全局 fallback policy 保护无关路由。
- **属性 API**：`McpServerTool` 无 `Description` 属性（只有 `Name`/`Title`/`ReadOnly` 等）。工具与参数描述用独立的 **`[Description("...")]`**（`System.ComponentModel`）。
- **Scope 策略（读写分离，工具内强制）**：`ddi.registry.read`（resolve_urn / list_agencies / get_services）与 `ddi.registry.write`（request_agency）。各工具内 `HasScope` 必须枚举所有 `scope` 与 `scp` claims，再按空白分隔其值；缺失时返回**明确的权限错误结果**（读工具不得静默返回空集合）。`request_agency` 显式要求 `ddi.registry.write`。**IdP 需为客户端签发相应 scope**（写客户端通常同时签发两者）；`HasScope` 不做 write→read 推导。
- MCP 服务**不运行 EF 迁移**；只连接一个已被 Web 应用迁移过的同一 PostgreSQL 库。
- **身份（仅 MCP 侧校验）**：`request_agency` 的 `CreatorId/AdminContactId/TechnicalContactId` 必须指向**已存在的 `AspNetUsers` 行**。先以 `email`/`ClaimTypes.Email` 匹配 **`NormalizedEmail`**（大写归一），未命中再以 `sub == Id` 回退；身份映射成功后才检查机构重复。并发重复写入：仅捕获 `Agencies` 主键导致的 `23505`，返回与「已存在」相同的拒绝结果。Web 应用保留本地 Identity 不变。经 AWS ALB 需配置只信任 ALB 私网 CIDR 或固定代理地址的 `ForwardedHeadersOptions` + `UseForwardedHeaders()`。
- **机构 ID/标签校验（与 Web 一致，且 ISO 文件以 Data 的单一内容文件存放）**：通过共享 `AgencyIdValidator`（位于 Data，引入 NISOCountries）。文件复制到每个应用输出目录，校验器以 `Path.Combine(AppContext.BaseDirectory, "iso3166-countrycodes.txt")` 调用 `RipeISOCountryReader.Parse(string path)`。ISO 数据缺失或读取失败时校验失败，不能按格式通过；Web 后续改用同一校验器即获得一致行为。
- 四个工具均用 `[McpServerTool]`（`[McpServerToolType]` 聚合）注解，附**英文** `Title` 与 `[Description]`，参数用 `[Description]`。
- 重构仅移动/抽取，**除 `DdiUrn.TryParse` 的 scheme 判断 bug 修正（改为 `||`）外**，不改变解析语义（URN 格式 `urn:ddi:{agency}:{identifier}:{version}`，`agency` 同时等于 `AssignmentId` 与 `AgencyId`）。

---

## 文件结构

**新建：**
- `src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
- `src/Ddi.Registry.Mcp/Program.cs`（含 `public partial class Program {}`）
- `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- `src/Ddi.Registry.Mcp/appsettings.json` / `appsettings.Development.json`
- `src/Ddi.Registry.Data/AgencyIdValidator.cs`
- `src/Ddi.Registry.Data/iso3166-countrycodes.txt` —— **唯一来源的内容文件**，复制到每个应用输出目录
- `src/Ddi.Registry.Mcp.Tests/`：`Ddi.Registry.Mcp.Tests.csproj`（引用 Data + MCP + Mvc.Testing）、`DdiUrnTests.cs`、`HttpResolverResolveUrlTests.cs`、`AgencyIdValidatorTests.cs`（无 DB）、`McpWebApplicationFactory.cs`、`TestAuthHandler.cs`、`McpHttpTestClient.cs`、`ToolIntegrationTests.cs`

**修改：**
- `src/Ddi.Registry.Data/DdiUrn.cs`（移入 + scheme `||`）
- `src/Ddi.Registry.Data/HttpResolver.cs`（新增 `ResolveUrl`）
- `src/Ddi.Registry.Data/Ddi.Registry.Data.csproj`（新增 NISOCountries + ISO 文件的复制配置）
- `src/Ddi.Registry.Web/Models/DdiUrn.cs`（删除）/ `ManageModels.cs`、`Controllers/AgencyController.cs`（补 `using`）
- `src/Ddi.Registry.Web/Views/Agency/Resolver.cshtml`（改用 `ResolveUrl`）
- `src/Ddi.Registry.Web/Controllers/ManageController.cs`（`AddAgency` 改用 `AgencyIdValidator`）
- `src/Ddi.Registry.Web/Ddi.Registry.Web.csproj`（移除直接 NISOCountries 引用与 Web 自带的 iso3166 文件；运行时 ISO 内容文件由 Data 项目复制到输出目录）
- `Ddi.Registry.Web.sln`（加入新工程）

---

### Task 1: 抽取共享解析与校验逻辑（移动 DdiUrn + 修正 scheme + ResolveUrl + AgencyIdValidator）

**Files:**
- Create: `src/Ddi.Registry.Data/AgencyIdValidator.cs`
- Create: `src/Ddi.Registry.Data/iso3166-countrycodes.txt`（从 Web 复制 → **唯一来源的内容文件**）
- Create: `src/Ddi.Registry.Mcp.Tests/{Ddi.Registry.Mcp.Tests.csproj, DdiUrnTests.cs, HttpResolverResolveUrlTests.cs, AgencyIdValidatorTests.cs}`（仅引用 **Data**）
- Move: `src/Ddi.Registry.Web/Models/DdiUrn.cs` → `src/Ddi.Registry.Data/DdiUrn.cs`（命名空间 `Ddi.Registry.Data`；scheme 判断改 `||`）
- Modify: `src/Ddi.Registry.Data/HttpResolver.cs`（新增 `ResolveUrl`）
- Modify: `src/Ddi.Registry.Data/Ddi.Registry.Data.csproj`（新增 NISOCountries + 复制 iso3166 内容文件）
- Modify: Web 的 `ManageModels.cs`/`AgencyController.cs`（补 `using`）、`Resolver.cshtml`（改用 `ResolveUrl`）、`ManageController.AddAgency`（改用 `AgencyIdValidator`）、Web csproj（移除 NISOCountries + 自有 iso 文件）
- Delete: `src/Ddi.Registry.Web/Models/DdiUrn.cs`

**Interfaces:**
- Produces: `DdiUrn.TryParse`（拒绝非 DDI scheme）、`HttpResolver.ResolveUrl(DdiUrn):string`、`AgencyIdValidator.Validate(string,string):(bool ok, string error)`。Task 3-6 依赖这些。

- [ ] **Step 1: 创建测试工程并写失败测试（仅引用 Data）**

`src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Ddi.Registry.Data\Ddi.Registry.Data.csproj" />
  </ItemGroup>
</Project>
```

`src/Ddi.Registry.Mcp.Tests/DdiUrnTests.cs`：
```csharp
using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class DdiUrnTests
    {
        [Fact] public void TryParse_ValidUrn_ParsesComponents() {
            var ok = DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn);
            Assert.True(ok); Assert.Equal("us.foo", urn.Agency); Assert.Equal("bar", urn.Identifier); Assert.Equal("1", urn.Version);
        }
        [Fact] public void TryParse_Agency_IsLowercased() { DdiUrn.TryParse("urn:ddi:US.Foo:bar:1", out var urn); Assert.Equal("us.foo", urn.Agency); }
        [Fact] public void TryParse_NotFiveParts_ReturnsFalse() => Assert.False(DdiUrn.TryParse("urn:ddi:us.foo:bar", out _));
        [Fact] public void TryParse_WrongSchemePrefix_ReturnsFalse() => Assert.False(DdiUrn.TryParse("http:ddi:us.foo:bar:1", out _));
        [Fact] public void TryParse_WrongSchemeSecondPart_ReturnsFalse() => Assert.False(DdiUrn.TryParse("urn:not-ddi:us.foo:bar:1", out _)); // 回归
        [Fact] public void ToString_RoundTrips() { DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("urn:ddi:us.foo:bar:1", urn.ToString()); }
    }
}
```

`src/Ddi.Registry.Mcp.Tests/HttpResolverResolveUrlTests.cs`：
```csharp
using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class HttpResolverResolveUrlTests
    {
        [Fact] public void ResolveUrl_FillsAllTokens() {
            var r = new HttpResolver { UrlTemplate = "https://{agency}.example.org/{identifier}/{version}" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://us.foo.example.org/bar/1", r.ResolveUrl(urn));
        }
        [Fact] public void ResolveUrl_FillsUrnToken() {
            var r = new HttpResolver { UrlTemplate = "https://resolver.example.org/lookup?u={urn}" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://resolver.example.org/lookup?u=urn:ddi:us.foo:bar:1", r.ResolveUrl(urn));
        }
        [Fact] public void ResolveUrl_NoTokens_ReturnsTemplateVerbatim() {
            var r = new HttpResolver { UrlTemplate = "https://static.example.org" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://static.example.org", r.ResolveUrl(urn));
        }
    }
}
```

`src/Ddi.Registry.Mcp.Tests/AgencyIdValidatorTests.cs`：
```csharp
using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class AgencyIdValidatorTests
    {
        [Theory]
        [InlineData("us.foo", "Foo", true)]
        [InlineData("uk.foo", "Foo", true)]
        [InlineData("int.foo", "Foo", true)]
        [InlineData("zz.foo", "Foo", false)]          // 非法 2 字符码
        [InlineData("usa.foo", "Foo", false)]          // 非 int 的 3 字符码
        [InlineData("us.", "Foo", false)]              // 缺少名称
        [InlineData("u.foo", "Foo", false)]            // 前缀太短
        [InlineData("us.foo.bar", "Foo", false)]       // 嵌套点号（正则禁止）
        [InlineData("us.foobaroverfiftytwocharacterslongggggggggggggggggggggggg", "Foo", false)] // >50
        public void Validate_ReturnsExpected(string id, string label, bool expectedOk)
            => Assert.Equal(expectedOk, AgencyIdValidator.Validate(id, label).Ok);

        [Fact] public void Validate_NullLabel_Fails()
            => Assert.False(AgencyIdValidator.Validate("us.foo", null).Ok);

        [Fact] public void Validate_UnknownTwoCharCode_Fails_WhenIsoPresent()
            => Assert.False(AgencyIdValidator.Validate("zz.foo", "Foo").Ok); // 经嵌入的 iso 数据校验
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `cd e:/GitHub/ddiregistry && dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`
Expected: 编译失败（CS0246：`DdiUrn` / `ResolveUrl` / `AgencyIdValidator` 不存在）。

- [ ] **Step 3: 实现重构**

1. 移动 `DdiUrn.cs` → `src/Ddi.Registry.Data/DdiUrn.cs`，命名空间改 `Ddi.Registry.Data`，scheme 判断改 `||`：
```csharp
if (parts[0].ToLower() != "urn" || parts[1].ToLower() != "ddi") { return false; }
```

2. `HttpResolver.cs` 顶部加 `using System;`，类内新增：
```csharp
public string ResolveUrl(DdiUrn urn)
{
    if (urn == null) throw new ArgumentNullException(nameof(urn));
    return UrlTemplate.Replace("{agency}", urn.Agency)
        .Replace("{identifier}", urn.Identifier)
        .Replace("{version}", urn.Version)
        .Replace("{urn}", urn.ToString());
}
```

3. 复制 `iso3166-countrycodes.txt` 到 `src/Ddi.Registry.Data/`，并将其复制到应用输出目录（Data csproj）。SDK 默认已将该文件纳入 `None`，因此必须使用 `Update`，不能重复 `Include`：
```xml
<ItemGroup>
    <None Update="iso3166-countrycodes.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

4. `src/Ddi.Registry.Data/AgencyIdValidator.cs`（以路径调用 NISOCountries；ISO 数据缺失或异常时拒绝请求）：
```csharp
using System;
using System.IO;
using NISOCountries.Core;
using NISOCountries.Ripe;

namespace Ddi.Registry.Data
{
    public static class AgencyIdValidator
    {
        public static (bool Ok, string Error) Validate(string agencyId, string label)
        {
            if (string.IsNullOrWhiteSpace(agencyId)) return (false, "An agency name is required.");
            if (string.IsNullOrWhiteSpace(label)) return (false, "An agency label is required.");
            if (agencyId.Length > 50) return (false, "The agency name must be 50 characters or fewer.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(agencyId, @"^[a-zA-Z]{2,3}\.[a-zA-Z0-9](-?[a-zA-Z0-9]+)*$"))
                return (false, "The agency name should be in the form [country code] dot [name], e.g. us.agencyname.");

            int index = agencyId.IndexOf('.');
            string code = agencyId.Substring(0, index);

            if (index == 2)
            {
                if (code.ToLowerInvariant() == "uk") return (true, null);
                try
                {
                    var isoFile = Path.Combine(AppContext.BaseDirectory, "iso3166-countrycodes.txt");
                    if (!File.Exists(isoFile)) return (false, "ISO country-code validation data is unavailable.");
                    var isoCountries = new RipeISOCountryReader().Parse(isoFile);
                    var isoLookup = new ISOCountryLookup<RipeCountry>(isoCountries);
                    if (isoLookup.TryGetByAlpha2(code, out _)) return (true, null);
                }
                catch (Exception) { return (false, "ISO country-code validation data is unavailable."); }
                return (false, $"{code} is not a valid country code. Use a 2-char ISO 3166 code or 'uk'.");
            }
            else if (index == 3 && code.ToLowerInvariant() == "int")
            {
                return (true, null);
            }
            return (false, "The agency id must start with a 2 character ISO 3166 country code or 'int', e.g. us.agencyname.");
        }
    }
}
```

5. `Ddi.Registry.Data.csproj` 增加：
```xml
<PackageReference Include="NISOCountries.Core" Version="1.2.0" />
<PackageReference Include="NISOCountries.Ripe" Version="1.2.0" />
```

6. Web 引用：`ManageModels.cs`、`AgencyController.cs` 顶部加 `using Ddi.Registry.Data;`。

7. `Resolver.cshtml:31-35` 改为 `string url = item.ResolveUrl(Model.Urn);`

8. `ManageController.AddAgency`（约 838-867 行）移除内联国家码/ISO 校验块，改为：
```csharp
var validation = AgencyIdValidator.Validate(addAgencyModel.AgencyId, addAgencyModel.Label);
if (!validation.Ok) ModelState.AddModelError("", validation.Error);
```
保留其后 `ModelState.IsValid` 分支与已存在性检查。

9. 删除 `src/Ddi.Registry.Web/Models/DdiUrn.cs`。Web csproj 移除 `NISOCountries.*` 包引用与自有 `iso3166-countrycodes.txt` 的 Content 项；Data 项目提供复制到输出目录的唯一 ISO 内容文件。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`
Expected: 全部 PASS（含 scheme 回归与 validator 测试）。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Data/ src/Ddi.Registry.Web/ src/Ddi.Registry.Mcp.Tests/
git commit -m "refactor: share DdiUrn/ResolveUrl/AgencyIdValidator in Data; fix URN scheme bug"
```

---

### Task 2: 搭建 MCP 工程脚手架（Program.cs + 包 + appsettings + ForwardedHeaders + 正确 OAuth 接线）

**Files:**
- Create: `src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
- Create: `src/Ddi.Registry.Mcp/Program.cs`（含 `public partial class Program {}`）
- Create: `src/Ddi.Registry.Mcp/appsettings.json` / `appsettings.Development.json`

**Interfaces:**
- Consumes: `ApplicationDbContext`、`DdiUrn`/`ResolveUrl`/`AgencyIdValidator`（Task 1）。
- Produces: 可启动的 MCP 宿主（Streamable HTTP `/mcp`）+ OAuth 受保护资源 + Bearer 验证 + ForwardedHeaders + fallback 授权策略。

- [ ] **Step 1: 创建 MCP 工程 csproj**

`src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="2.0.0" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="2.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.2" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.2" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ddi.Registry.Data\Ddi.Registry.Data.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 还原包（固定 2.0.0）并核对真实 API 面**

Run:
```bash
cd e:/GitHub/ddiregistry/src/Ddi.Registry.Mcp
dotnet add package ModelContextProtocol --version 2.0.0
dotnet add package ModelContextProtocol.AspNetCore --version 2.0.0
```
在 `~/.nuget/packages/modelcontextprotocol.aspnetcore/2.0.0/` 下读取 `ModelContextProtocol.AspNetCore.xml`（或反射），核对：
- 服务器注册：**`AddMcpServer()` + `WithHttpTransport(options => options.Stateless = true)` + `WithTools<T>()`**（Streamable HTTP）。
- **认证扩展 `AddMcp`**：确认其为 **`AuthenticationBuilder` 扩展**，签名为 `.AddMcp(Action<McpAuthenticationOptions>)`，可配置 `ResourceMetadata` / `ResourceMetadataUri` 与 metadata 事件；MCP 处理器在 401 的 `WWW-Authenticate` challenge 中提供 `/.well-known/oauth-protected-resource/mcp` 的 metadata URI。
- `MapMcp("/mcp")`、`McpServerTool`/`McpServerToolType` 命名空间（预期 `ModelContextProtocol.Server`），确认 `[Description]` 用于描述。
- 测试客户端：2.0.0 已还原包未提供可直接用于 `WebApplicationFactory.HttpClient` 的公开 Streamable HTTP client transport；不得臆测 `StreamableHttpClientTransport` / `SseClientTransport` 类型。Task 8 创建受控的 `McpHttpTestClient`，以该版本已验证的 Streamable HTTP JSON-RPC 请求、必需 `Accept` 头和 `/mcp` 路径驱动协议测试；运行期冒烟使用真实 MCP Inspector 或 Claude Desktop 验证第三方客户端互操作。

版本固定 2.0.0；若不兼容 net10，取 2.0.0 之后兼容 net10 的最低补丁版本并固定。

- [ ] **Step 3: 写 Program.cs（认证接线：AddAuthentication().AddJwtBearer().AddMcp()）**

`src/Ddi.Registry.Mcp/Program.cs`：
```csharp
using Ddi.Registry.Data;
using Ddi.Registry.Mcp.Tools;            // WithTools<RegistryTools>() 所需类型可见性
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;    // 按 Step 2 核实命名空间

var builder = WebApplication.CreateBuilder(args);

// AWS ALB 转发头：仅信任部署配置中的反向代理，不可清空 KnownNetworks/KnownProxies。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    var trustedProxy = builder.Configuration["MCP:ReverseProxy:TrustedProxy"];
    if (!builder.Environment.IsDevelopment() && !System.Net.IPAddress.TryParse(trustedProxy, out var proxyAddress))
        throw new InvalidOperationException("MCP:ReverseProxy:TrustedProxy must be a trusted ALB/reverse-proxy IP address outside Development.");
    if (proxyAddress is not null)
        options.KnownProxies.Add(proxyAddress);
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();

// 服务器注册（Streamable HTTP；legacy SSE 默认关闭）
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<RegistryTools>();

// 认证接线：Bearer 认证；McpAuth 为 401 challenge 提供 protected-resource metadata
var oidcAuthority = builder.Configuration["MCP:Oidc:Authority"];
var oidcAudience = builder.Configuration["MCP:Oidc:Audience"];
var oidcScopes = (builder.Configuration["MCP:Oidc:Scopes"] ?? string.Empty)
    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
var authenticationEnabled = !string.IsNullOrWhiteSpace(oidcAuthority);

if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(oidcAuthority) || string.IsNullOrWhiteSpace(oidcAudience)))
    throw new InvalidOperationException("MCP:Oidc:Authority and MCP:Oidc:Audience are required outside Development.");

if (authenticationEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Bearer";
        options.DefaultChallengeScheme = "McpAuth";
    })
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = oidcAuthority;
        options.Audience = oidcAudience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    })
    .AddMcp(options =>
    {
        // ResourceMetadata describes this MCP protected resource. Its
        // AuthorizationServers collection contains the external IdP authority.
        options.ResourceMetadata = new()
        {
            AuthorizationServers = [oidcAuthority!],
            ScopesSupported = [.. oidcScopes]
        };
        // Leave ResourceMetadataUri unset: SDK serves the default
        // /.well-known/oauth-protected-resource/mcp resource metadata.
    });

    builder.Services.AddAuthorization();
}

var app = builder.Build();

app.UseForwardedHeaders();

if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var mcpEndpoint = app.MapMcp("/mcp");
if (authenticationEnabled)
    mcpEndpoint.RequireAuthorization(new AuthorizeAttribute());

app.Run();

public partial class Program { }   // 供 WebApplicationFactory 使用
```

`src/Ddi.Registry.Mcp/appsettings.json`：
```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=ddiregistry;Username=ddi;Password=CHANGE_ME" },
  "MCP": {
    "Oidc": { "Authority": "", "Audience": "", "Scopes": "ddi.registry.read ddi.registry.write" },
    "ReverseProxy": { "TrustedProxy": "" }
  },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } }
}
```

`src/Ddi.Registry.Mcp/appsettings.Development.json`：
```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=ddiregistry;Username=postgres;Password=postgres" }
}
```

- [ ] **Step 4: 编译验证宿主可构建（先创建空 RegistryTools 骨架）**

Run: `cd e:/GitHub/ddiregistry && dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功（`RegistryTools` 在 Task 3 填充）。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Mcp/
git commit -m "feat(mcp): scaffold MCP host (Streamable HTTP) with OAuth, DbContext, ForwardedHeaders"
```

---

### Task 3: 实现 resolve_urn 工具

**Files:**
- Create: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`（含 `ResolveUrn` + 结果 + `HasScope`）

**Interfaces:**
- Consumes: `ApplicationDbContext`、`DdiUrn.TryParse`、`HttpResolver.ResolveUrl`（Task 1）。
- Produces: `[McpServerToolType] RegistryTools`（构造注入 `ApplicationDbContext` + `IHttpContextAccessor`）。后续 Task 追加方法。

- [ ] **Step 1: 创建工具类（方法/参数描述用 `[Description]`）**

`src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`：
```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;   // 按 Task2 Step2 核实

namespace Ddi.Registry.Mcp.Tools
{
    [McpServerToolType]
    public class RegistryTools
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RegistryTools(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        [McpServerTool(Name = "resolve_urn", Title = "Resolve DDI URN")]
        [Description("Resolve a DDI URN (urn:ddi:{agency}:{identifier}:{version}) to its HTTP resolution endpoints. Only works for Approved agencies. Requires scope ddi.registry.read.")]
        public async Task<ResolveUrnResult> ResolveUrn(
            [Description("DDI URN, e.g. urn:ddi:us.foo:bar:1")] string urn)
        {
            if (!HasScope("ddi.registry.read"))
                return new ResolveUrnResult { Found = false, Message = "Missing required scope 'ddi.registry.read'." };

            if (!DdiUrn.TryParse(urn, out var ddiUrn))
                return new ResolveUrnResult { Found = false, Message = $"Cannot parse URN: {urn}. Expected urn:ddi:{{agency}}:{{identifier}}:{{version}}." };

            var assignment = await _context.Assignments
                .Include(a => a.HttpResolvers)
                .Include(a => a.Agency)
                .FirstOrDefaultAsync(a => a.AssignmentId == ddiUrn.Agency &&
                    a.Agency.ApprovalState == ApprovalState.Approved);
            if (assignment == null)
                return new ResolveUrnResult { Found = false, Message = $"No agency assignment found for {ddiUrn.Agency}. The URN may not be approved." };

            var endpoints = new List<ResolveEndpoint>();
            foreach (var r in assignment.HttpResolvers)
                endpoints.Add(new ResolveEndpoint { ResolutionType = r.ResolutionType, Url = r.ResolveUrl(ddiUrn) });

            return new ResolveUrnResult { Found = true, AgencyId = ddiUrn.Agency, AgencyLabel = assignment.Agency.Label, Endpoints = endpoints };
        }

        // Scope values can be emitted as multiple scope/scp claims by an IdP.
        private bool HasScope(string requiredScope)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity.IsAuthenticated) return false;
            return user.Claims
                .Where(c => c.Type == "scope" || c.Type == "scp")
                .SelectMany(c => c.Value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
                .Contains(requiredScope, System.StringComparer.Ordinal);
        }
    }

    public class ResolveUrnResult
    {
        public bool Found { get; set; }
        public string? AgencyId { get; set; }
        public string? AgencyLabel { get; set; }
        public string? Message { get; set; }
        public List<ResolveEndpoint> Endpoints { get; set; } = new();
    }
    public class ResolveEndpoint { public string? ResolutionType { get; set; } public string? Url { get; set; } }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功。

- [ ] **Step 3: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs
git commit -m "feat(mcp): add resolve_urn tool"
```

---

### Task 4: 实现 list_agencies 工具（EF.Functions.ILike）

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`（新增 `ListAgencies` + `AgencySummary`）

**Interfaces:**
- Consumes: `ApplicationDbContext`、`ApprovalState`。
- Produces: `ListAgencies(country?)` 返回所有审批状态；前缀过滤用 `EF.Functions.ILike`（escaped）。scope 缺失时返回**明确错误**（非静默空集合）。

- [ ] **Step 1: 新增 ListAgencies（ILike + 明确错误处理）**

在 `RegistryTools` 追加：
```csharp
[McpServerTool(Name = "list_agencies", Title = "List Agencies")]
[Description("List DDI agencies. Optional country filters by AgencyId prefix ({countryCode}.). Returns all approval states (Requested/Approved/Deprecated/None). Requires scope ddi.registry.read.")]
public async Task<ListAgenciesResult> ListAgencies(
    [Description("ISO country-code prefix, e.g. \"us\"; empty returns all")] string? country = null)
{
    if (!HasScope("ddi.registry.read"))
        return new ListAgenciesResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

    var query = _context.Agencies.AsQueryable();
    if (!string.IsNullOrWhiteSpace(country))
    {
        var escaped = country.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        query = query.Where(a => EF.Functions.ILike(a.AgencyId, escaped + ".%"));
    }

    var agencies = await query.OrderBy(a => a.AgencyId)
        .Select(a => new AgencySummary
        {
            AgencyId = a.AgencyId, Label = a.Label, ApprovalState = a.ApprovalState,
            DateCreated = a.DateCreated, DateApproved = a.DateApproved
        }).ToListAsync();

    return new ListAgenciesResult { Ok = true, Agencies = agencies };
}
```

追加：
```csharp
public class ListAgenciesResult { public bool Ok { get; set; } public string? Message { get; set; } public List<AgencySummary> Agencies { get; set; } = new(); }
public class AgencySummary { public string? AgencyId { get; set; } public string? Label { get; set; } public ApprovalState ApprovalState { get; set; } public DateTime DateCreated { get; set; } public DateTime? DateApproved { get; set; } }
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功。

- [ ] **Step 3: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs
git commit -m "feat(mcp): add list_agencies tool (ILike prefix filter, explicit scope error)"
```

---

### Task 5: 实现 get_services 工具

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`（新增 `GetServices` + `ServiceSummary`）

**Interfaces:**
- Consumes: `ApplicationDbContext.GetServicesForAssignment(assignmentId)`。
- Produces: `GetServices(assignmentId)`；scope 缺失时返回明确错误（非静默空集合）。

- [ ] **Step 1: 新增 GetServices（明确错误处理）**

```csharp
[McpServerTool(Name = "get_services", Title = "Get Agency Services")]
[Description("Return all DNS SRV-style service records for the given Assignment (i.e. AgencyId). Requires scope ddi.registry.read.")]
public async Task<GetServicesResult> GetServices(
    [Description("AssignmentId, usually equal to AgencyId, e.g. us.foo")] string assignmentId)
{
    if (!HasScope("ddi.registry.read"))
        return new GetServicesResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

    var services = await _context.GetServicesForAssignment(assignmentId);
    return new GetServicesResult
    {
        Ok = true,
        Services = services.Select(s => new ServiceSummary
        {
            ServiceId = s.ServiceId, Hostname = s.Hostname, Port = s.Port,
            ServiceName = s.ServiceName, Protocol = s.Protocol, Priority = s.Priority,
            Weight = s.Weight, TimeToLive = s.TimeToLive
        }).ToList()
    };
}
```

```csharp
public class GetServicesResult { public bool Ok { get; set; } public string? Message { get; set; } public List<ServiceSummary> Services { get; set; } = new(); }
public class ServiceSummary { public string? ServiceId { get; set; } public string? Hostname { get; set; } public int Port { get; set; } public string? ServiceName { get; set; } public string? Protocol { get; set; } public int Priority { get; set; } public int Weight { get; set; } public int TimeToLive { get; set; } }
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功。

- [ ] **Step 3: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs
git commit -m "feat(mcp): add get_services tool (explicit scope error)"
```

---

### Task 6: 实现 request_agency 工具（受控写入 + IdP 映射 + 共享校验 + write scope + 并发重复）

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`（新增 `RequestAgency` + `RequestAgencyResult`）

**Interfaces:**
- Consumes: `ApplicationDbContext`、`AgencyIdValidator`（Task 1）、`IHttpContextAccessor`（Task 2）。
- Produces: `RequestAgency(label, org)`：校验 `ddi.registry.write` → `AgencyIdValidator` → 查重 → 身份映射（NormalizedEmail / sub）→ 插入 `Requested`；捕获唯一约束异常返回重复结果。

- [ ] **Step 1: 新增 RequestAgency（含 write scope、并发重复处理）**

```csharp
[McpServerTool(Name = "request_agency", Title = "Request New Agency")]
[Description("Submit a new DDI agency identifier request (state=Requested). org is the suggested AgencyId, validated exactly like the web app (ISO 3166 / int / uk). Caller identity is mapped from the validated external IdP token to an existing AspNetUsers row. Requires scope ddi.registry.write. No email is sent.")]
public async Task<RequestAgencyResult> RequestAgency(
    [Description("Agency display label")] string label,
    [Description("Suggested AgencyId, e.g. us.myorg")] string org)
{
    if (!HasScope("ddi.registry.write"))
        return new RequestAgencyResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

    var validation = AgencyIdValidator.Validate(org, label);
    if (!validation.Ok) return new RequestAgencyResult { Success = false, Message = validation.Error };

    var user = _httpContextAccessor.HttpContext?.User;
    if (user == null || !user.Identity.IsAuthenticated)
        return new RequestAgencyResult { Success = false, Message = "No valid identity token presented." };

    var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
    var sub = user.FindFirst("sub")?.Value;
    var account = !string.IsNullOrWhiteSpace(email)
        ? await _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
        : null;
    account ??= !string.IsNullOrWhiteSpace(sub) ? await _context.Users.FindAsync(sub) : null;
    if (account == null)
        return new RequestAgencyResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

    var existing = await _context.Agencies.FindAsync(org);
    if (existing != null) return new RequestAgencyResult { Success = false, Message = $"Agency identifier {org} already exists." };

    var agency = new Agency
    {
        AgencyId = org, Label = label, ApprovalState = ApprovalState.Requested,
        CreatorId = account.Id, AdminContactId = account.Id, TechnicalContactId = account.Id
    };
    _context.Agencies.Add(agency);

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsAgencyPrimaryKeyViolation(ex))
    {
        // 并发重复请求：与「已存在」返回相同拒绝结果
        return new RequestAgencyResult { Success = false, Message = $"Agency identifier {org} already exists." };
    }

    return new RequestAgencyResult { Success = true, AgencyId = org, ApprovalState = ApprovalState.Requested, Message = $"Agency {org} submitted with state Requested; pending admin approval." };
}

// 判定 Postgres 唯一约束冲突（23505）；无法判定时保守视为非唯一冲突，向上抛出
private static bool IsAgencyPrimaryKeyViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
{
    var pgEx = ex.InnerException as Npgsql.PostgresException;
    return pgEx?.SqlState == "23505" && pgEx.ConstraintName == "PK_Agencies";
}
```

```csharp
public class RequestAgencyResult { public bool Success { get; set; } public string? AgencyId { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功（`using Npgsql;` 或完全限定 `Npgsql.PostgresException`）。

- [ ] **Step 3: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs
git commit -m "feat(mcp): add request_agency tool (IdP mapping, shared validation, write scope, concurrency)"
```

---

### Task 7: 确认 OAuth 受保护资源接线与 metadata 路径一致性

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Program.cs`（依据 Task 2 Step 2 还原包结果收口）
- （删除任何手写 `/.well-known/oauth-authorization-server` 重定向；授权服务器 metadata 由外部 IdP 原生提供）

**Interfaces:**
- Consumes: `MCP:Oidc:*` 配置。
- Produces: 启用时 `/mcp` 需有效 IdP 令牌（含所需 scope）；`/.well-known/oauth-protected-resource/mcp` 由 SDK 认证扩展匿名暴露，标准客户端经 401 的 `WWW-Authenticate` 自动发现授权服务器。

- [ ] **Step 1: 收口 AddMcp protected-resource metadata**

按 Task 2 Step 2 核实的 `McpAuthenticationOptions` 实际类型，设置 `ResourceMetadata.AuthorizationServers` 为外部 IdP authority/issuer。保留 `ResourceMetadataUri` 未设置，让 SDK 在 `/.well-known/oauth-protected-resource/mcp` 暴露 MCP 服务自身的 metadata；不得将该属性或 `ResourceMetadata` 指向 `https://<idp>/.well-known/openid-configuration`。**不要**手写 `/.well-known/oauth-authorization-server` 路由——外部 IdP 原生提供。

- [ ] **Step 2: 确认认证、challenge 与本地门控**

Authority 已配置时，确认 `DefaultAuthenticateScheme = "Bearer"`、`DefaultChallengeScheme = "McpAuth"`，并使用不指定 scheme 的 `AuthorizationPolicyBuilder().RequireAuthenticatedUser()`。这样认证使用 Bearer，而 401 challenge 使用 MCP 处理器。Authority 未配置时，跳过 `AddJwtBearer`、`AddMcp`、`AddAuthorization` 以及 `UseAuthentication`/`UseAuthorization`，保持本地匿名运行。

用认证开启的测试宿主请求 `/mcp`，断言 401 的 `WWW-Authenticate` 包含 protected-resource metadata URI；再断言 `/.well-known/oauth-protected-resource/mcp` 匿名可访问。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
Expected: 构建成功。

- [ ] **Step 4: 提交**

```bash
git add src/Ddi.Registry.Mcp/Program.cs
git commit -m "feat(mcp): finalize OAuth protected-resource wiring and metadata path"
```

---

### Task 8: 集成测试（使用 MCP 客户端 SDK，必需断言）

**Files:**
- Create: `src/Ddi.Registry.Mcp.Tests/McpWebApplicationFactory.cs`
- Create: `src/Ddi.Registry.Mcp.Tests/TestAuthHandler.cs`
- Create: `src/Ddi.Registry.Mcp.Tests/McpHttpTestClient.cs`
- Create: `src/Ddi.Registry.Mcp.Tests/ToolIntegrationTests.cs`

**Interfaces:**
- Consumes: `Ddi.Registry.Mcp`（此时已存在）+ Task 1-7 产物（**无循环依赖**：Task 1 测试仅引用 Data；本 Task 测试引用已建成的 MCP 工程）。
- Produces: `WebApplicationFactory<Program>` 在认证开启模式下拉起宿主；普通工具测试替换真实 PG 为 InMemory，注入由请求头驱动的 Test 认证方案；种子本地用户 + Approved 机构 + Assignment + HttpResolver。测试项目新增 `McpHttpTestClient`，集中封装已验证的 Streamable HTTP JSON-RPC 初始化、`tools/list` 与 `tools/call` 请求；测试不得引用不存在的 SDK transport。并发唯一约束测试另用 PostgreSQL Testcontainers，不能用 InMemory 替代。

- [ ] **Step 1: 测试工程加 MVC.Testing + MCP 客户端引用**

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.2" />
<ItemGroup>
  <ProjectReference Include="..\..\Ddi.Registry.Mcp\Ddi.Registry.Mcp.csproj" />
  <ProjectReference Include="..\..\Ddi.Registry.Data\Ddi.Registry.Data.csproj" />
</ItemGroup>
```

随后运行 `dotnet add package Testcontainers.PostgreSql`，核对 net10.0 兼容性后将还原出的具体版本锁定在测试 csproj；该包为 PostgreSQL `23505` 集成测试所必需。

- [ ] **Step 2: 测试认证处理器与工厂（替换 DB + 种子）**

`TestAuthHandler.cs`：
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Ddi.Registry.Mcp.Tests
{
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string HeaderName = "X-Test-Principal";
        public const string EmailClaim = "test@example.com";
        public const string SeedUserId = "mcp-test-user";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var principal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = principal.ToString() switch
            {
                "full" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "ddi.registry.read ddi.registry.write") },
                "read" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "ddi.registry.read") },
                "unknown" => new[] { new Claim(ClaimTypes.Email, "unknown@example.com"), new Claim("scope", "ddi.registry.write") },
                "sub" => new[] { new Claim("sub", SeedUserId), new Claim("scope", "ddi.registry.write") },
                // Regression case: required scope is in the second same-name claim.
                "multi-read" => new[] { new Claim(ClaimTypes.Email, EmailClaim), new Claim("scope", "unrelated"), new Claim("scope", "ddi.registry.read") },
                _ => Array.Empty<Claim>()
            };
            if (claims.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
```

`McpWebApplicationFactory.cs`：
```csharp
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Linq;

namespace Ddi.Registry.Mcp.Tests
{
    public class McpWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MCP:Oidc:Authority"] = "https://test-idp.invalid"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("mcp-test"));
                // Program's fallback policy has no named scheme. Override only
                // authentication for tests; retain McpAuth as the challenge scheme.
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = "McpAuth";
                })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }

        public void Seed()
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (ctx.Users.Any()) return;
            var user = new ApplicationUser { Id = TestAuthHandler.SeedUserId, UserName = "tester", Email = TestAuthHandler.EmailClaim, NormalizedEmail = TestAuthHandler.EmailClaim.ToUpperInvariant() };
            ctx.Users.Add(user);
            var agencyId = "us.testorg";
            ctx.Agencies.Add(new Agency { AgencyId = agencyId, Label = "Test Org", ApprovalState = ApprovalState.Approved, CreatorId = user.Id, AdminContactId = user.Id, TechnicalContactId = user.Id });
            ctx.Assignments.Add(new Assignment { AssignmentId = agencyId, AgencyId = agencyId });
            ctx.HttpResolvers.Add(new HttpResolver { AssignmentId = agencyId, ResolutionType = HttpResolver.ServiceNameWeb, UrlTemplate = "https://{agency}.example.org/{identifier}" });
            ctx.SaveChanges();
        }
    }
}
```

- [ ] **Step 3: 集成测试（必需断言）**

新增 `McpHttpTestClient.cs`：接收 `HttpClient` 和 `/mcp`，实现 `InitializeAsync`、`ListToolsAsync`、`CallToolAsync`。它必须为每次 POST 同时发送 `Accept: application/json, text/event-stream`，序列化 MCP 2.0.0 支持的初始化与工具 JSON-RPC 请求，并将响应中的结构化 `result` / `error` 返回给测试。它不是生产 MCP 客户端，所有调用均经过 `WebApplicationFactory` 的 `HttpClient`，从而保留 `X-Test-Principal` 请求头。

`ToolIntegrationTests.cs`：
```csharp
using Ddi.Registry.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class ToolIntegrationTests
    {
        private static async Task<McpHttpTestClient> ConnectAsync(McpWebApplicationFactory factory, string? principal = "full")
        {
            var httpClient = factory.CreateClient();
            if (principal != null)
                httpClient.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, principal);
            var client = new McpHttpTestClient(httpClient, "/mcp");
            await client.InitializeAsync();
            return client;
        }

        [Fact]
        public async Task ToolsList_ReturnsExactlyFourTools()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var tools = await client.ListToolsAsync();
            var names = tools.Select(t => t.Name).ToHashSet();
            Assert.Equal(new[] { "resolve_urn", "list_agencies", "get_services", "request_agency" }, names.OrderBy(x => x));
            Assert.Equal(4, names.Count);
        }

        [Fact]
        public async Task Unauthenticated_Initialize_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            var response = await McpHttpTestClient.SendInitializeAsync(factory.CreateClient(), "/mcp");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", response.Headers.WwwAuthenticate.ToString());
            var metadata = await factory.CreateClient().GetAsync("/.well-known/oauth-protected-resource/mcp");
            Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
            Assert.Contains("ddi.registry.read", await metadata.Content.ReadAsStringAsync());
            Assert.Contains("ddi.registry.write", await metadata.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ResolveUrn_AfterApproval_ReturnsFilledUrl()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:ddi:us.testorg:bar:1" });
            // 断言结果中含 https://us.testorg.example.org/bar
            Assert.Contains("https://us.testorg.example.org/bar", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_NonDdiScheme_ReturnsError()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:not-ddi:us.testorg:bar:1" });
            Assert.Contains("Cannot parse", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_DeprecatedAgency_IsNotResolved()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Agencies.FindAsync("us.testorg"))!.ApprovalState = ApprovalState.Deprecated;
            await context.SaveChangesAsync();
            var result = await (await ConnectAsync(factory)).CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:ddi:us.testorg:bar:1" });
            Assert.Contains("may not be approved", result.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_CreatesRequestedAgency()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "New Org", ["org"] = "us.neworg" });
            Assert.Contains("Requested", result.Content.ToString());
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull(await ctx.Agencies.FindAsync("us.neworg"));
        }

        [Fact]
        public async Task RequestAgency_Duplicate_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "us.dup" });
            var second = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "us.dup" });
            Assert.Contains("already exists", second.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_InvalidId_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory);
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "X", ["org"] = "zz.bad" });
            Assert.Contains("not a valid country code", result.Content.ToString());
        }

        [Fact]
        public async Task IdentityMapping_UnknownEmail_Rejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "unknown");
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Unknown", ["org"] = "us.unknown" });
            Assert.Contains("could not be mapped", result.Content.ToString());
        }

        [Fact]
        public async Task IdentityMapping_SubFallback_CreatesAgency()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var result = await (await ConnectAsync(factory, "sub")).CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Sub user", ["org"] = "us.subuser" });
            Assert.Contains("Requested", result.Content.ToString());
        }

        [Fact]
        public async Task RequestAgency_ReadOnlyScope_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("request_agency", new Dictionary<string, object> { ["label"] = "Read only", ["org"] = "us.readonly" });
            Assert.Contains("Missing required scope", result.Content.ToString());
        }

        [Fact]
        public async Task ResolveUrn_ScopeInSecondClaim_IsAccepted()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();
            var client = await ConnectAsync(factory, "multi-read");
            var result = await client.CallToolAsync("resolve_urn", new Dictionary<string, object> { ["urn"] = "urn:ddi:us.testorg:bar:1" });
            Assert.Contains("https://us.testorg.example.org/bar", result.Content.ToString());
        }
    }
}
```
> 必需断言包括：恰好四工具、无 `MCP-Session-Id` 响应头、未认证初始化 401 且 metadata challenge 正确、metadata 发布 read/write scopes、审批后解析、Deprecated 拒绝解析、非 DDI 报错、合法创建、重复拒绝、非法 ID 拒绝、read/write scope 拒绝、多同名 scope claim、email 与 sub 身份映射。不得以「按 SDK 契约补全」跳过任何断言。

- [ ] **Step 4: PostgreSQL 并发唯一约束测试**

新增 `PostgresRequestAgencyTests.cs`，使用 `Testcontainers.PostgreSql` 启动空 PostgreSQL 实例；测试宿主连接该实例并用 `EnsureCreatedAsync` 初始化测试 schema（生产 MCP 仍不运行迁移）。创建两个独立的认证 `McpHttpTestClient`，并发为同一合法 `org` 调用 `request_agency`。断言恰好一个结果成功，另一个结果含 `already exists`；该测试必须在 CI 的 Docker 环境执行，以实际 `Npgsql.PostgresException` 的 `23505` 覆盖 `IsAgencyPrimaryKeyViolation` 分支。

- [ ] **Step 5: 运行测试**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj`
Expected: 单元测试 + 集成测试 PASS。

- [ ] **Step 6: 提交**

```bash
git add src/Ddi.Registry.Mcp.Tests/
git commit -m "test(mcp): integration tests via MCP client — discovery, 401, resolve, create, duplicate, validation"
```

---

### Task 9: 接入解决方案 + 全量构建/测试

**Files:**
- Modify: `Ddi.Registry.Web.sln`

- [ ] **Step 1: 加入解决方案**

Run:
```bash
cd e:/GitHub/ddiregistry
dotnet sln Ddi.Registry.Web.sln add src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj
```

- [ ] **Step 2: 全量构建 + 测试**

Run: `dotnet build Ddi.Registry.Web.sln && dotnet test Ddi.Registry.Web.sln`
Expected: 构建成功；单测 + 集成测试 PASS；Web 原有行为不变。

- [ ] **Step 3: 运行期冒烟（需已迁移 PG + 已配置 Authority）**

配置 `appsettings.Development.json` 指向 PG 并配置 `MCP:Oidc:Authority`，用 MCP Inspector / Claude Desktop（OAuth）连接 `http://localhost:5000/mcp`：
- 四工具可见；`/.well-known/oauth-protected-resource/mcp` 匿名可达；`resolve_urn` 非 DDI 报错；`list_agencies("us")` 用 ILike 返回 `us.` 前缀；`get_services` 返回服务；`request_agency` 校验拒绝非法 ID、拒绝重复、合法 org 以 Requested 创建并归属映射用户；scope 缺失返回明确错误；无令牌 401。
- 无可连 PG 时跳过并标注。

- [ ] **Step 4: 提交**

```bash
git add Ddi.Registry.Web.sln
git commit -m "chore: add MCP projects to solution and verify build/tests"
```

---

## 自审（对照 spec）

- **Spec 覆盖**：传输（Streamable HTTP）→ Task 2/7；重构 → Task 1；共享校验 → Task 1 + Task 6；四工具 + IdP 映射 + OAuth + ForwardedHeaders + 读写 scope → Task 3-7；成功标准 → Task 1 单测 + Task 8 集成测试 + Task 9。
- **阻断修复（本轮）**：OAuth 接线以 Bearer 为默认 authenticate scheme、`McpAuth` 为默认 challenge scheme；授权策略不再固定 `Bearer`。`ResourceMetadata.AuthorizationServers` 指向外部 IdP，SDK 默认 endpoint 提供 MCP protected-resource metadata；删除手写授权服务器重定向。
- **高修复**：scope 策略一致——fallback 仅要求已认证 Bearer；读/写 scope 在工具内 `HasScope` 强制；读工具缺失 scope 返回**明确错误**（非静默空集合）；写需显式 `ddi.registry.write`，无 write→read 推导（IdP 签发相应 scope）。`McpServerTool` 无 `Description` → 全用 `[Description]`。
- **中高修复**：集成测试不再弱断言——使用 MCP 客户端 SDK，必需断言恰好四工具、未认证拒绝、审批后解析、非 DDI 报错、合法创建、重复拒绝、非法 ID 拒绝、身份映射失败。
- **中修复**：ISO 文件作为 Data 项目的单一内容文件复制到输出目录，校验器调用 `Parse(path)`，且数据不可用时拒绝请求；并发重复写入捕获 PostgreSQL `23505` 并返回重复结果。
- **占位符扫描**：版本固定 2.0.0；Task 2/7/8 注明还原包后核对确切类型（以实际 API 为准，不臆测）；集成测试为必需断言清单，非跳过占位。
- **类型一致性**：`DdiUrn`/`ResolveUrl`/`AgencyIdValidator.Validate`/`ApprovalState`/`Agency`/`ApplicationUser`/`GetServicesForAssignment` 命名一致；结果类型命名统一。
