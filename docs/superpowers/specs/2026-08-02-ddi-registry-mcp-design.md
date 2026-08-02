# DDI 注册表 MCP Server 设计文档

- **日期**：2026-08-02
- **状态**：已确认设计，待评审
- **范围**：将 DDI Agency Registry 的查询与（受限的）提交能力，封装为基于 Model Context Protocol (MCP) 的远程工具服务。

## 1. 目标与范围

把 `Ddi.Registry.Data` 中现有的查询能力以 MCP 工具的形式暴露出来，供 LLM/智能体在远程方式下调用。本期包含四个工具，其中三个只读、一个受控写入：

- `resolve_urn(urn)` —— 只读，包装 HttpResolver 模板填充逻辑
- `list_agencies(country?)` —— 只读，包装 RegistryProvider 查询
- `get_services(assignmentId)` —— 只读，包装 RegistryProvider 查询
- `request_agency(label, org)` —— 受控写入，提交审批流（`ApprovalState.Requested`）

**不在本期范围**：审批（Approve）、删除、解析器/服务编辑、用户/角色管理、DNS ZoneWriter 相关功能。这些保持仅在现有 Web 应用中可用。

## 2. 方案选择

### 选定方案：直接复用数据层的独立 MCP 项目
新建 ASP.NET Core 项目 `src/Ddi.Registry.Mcp`，以项目引用方式依赖 `Ddi.Registry.Data`，通过 `ApplicationDbContext` 连接**与 Web 应用相同的 PostgreSQL 数据库**，直接调用 `RegistryProvider` 扩展方法与 EF Core。

- 优点：真正实现「把 RegistryProvider 的查询能力暴露成工具」；共享同一领域模型与查询逻辑；代码量最小；单一数据真相源。
- 代价：MCP 服务与 Web 应用共享数据库，需约定「谁拥有 Schema」（见第 5、6 节）。

### 否决方案：在现有 Web REST API 前做 HTTP 代理
- 需要新增 API 端点、重复实现校验、并被 Web 应用的 Identity/Cookie 鉴权耦合。
- 不如直接复用数据层简洁，故否决。

### 传输方式
使用 `ModelContextProtocol` 的 ASP.NET Core 集成（2.0.0）：`AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithTools<RegistryTools>()` 配合 `MapMcp("/mcp")`，以 **Streamable HTTP**（MCP 2.0 的 HTTP 传输；legacy SSE 默认关闭）形式对外暴露，部署在现有 AWS 负载均衡（HTTPS 终止）之后。显式无状态模式允许 ALB 多实例负载均衡而无需 session affinity。

> 无状态模式不支持 legacy SSE、服务端主动请求、sampling、elicitation 或 roots。本期工具只执行请求-响应调用，故不需要这些能力。若未来必须兼容 legacy SSE 客户端，需显式启用 legacy SSE 传输，并在 ALB 多实例部署下配置会话亲和（session affinity）/ 有状态策略。

- MCP 服务**不运行 EF 迁移**：数据库 Schema 与迁移由 Web 应用负责；MCP 仅连接到一个已被迁移过的数据库。

## 3. 必要的重构（避免 Web 与 MCP 两份实现）

为保证单一真相源，做三处重构：

1. **移动 `DdiUrn`**：从 `Ddi.Registry.Web.Models` 移动到 `Ddi.Registry.Data`（它是领域类型；MCP 不应引入整个 Web/Identity 技术栈）。同步修改 Web 中各处的命名空间引用。
2. **修正 `DdiUrn.TryParse` 的 scheme 判断 bug**：现 `Resolver.cshtml`/`DdiUrn.cs:32` 用 `&&` 组合「非 urn」与「非 ddi」判断，导致 `urn:not-ddi:...` 这类非 DDI URN 被错误接受。改为 `||`，使 scheme 任一部分不符即拒绝——这是 `resolve_urn` 满足 DDI URN 语义的硬要求。
3. **新增 `HttpResolver.ResolveUrl(DdiUrn urn)`**：把硬编码在 `Resolver.cshtml:31-35` 的四次 `Replace("{agency}" / "{identifier}" / "{version}" / "{urn}")` 逻辑，封装为 `Ddi.Registry.Data` 中的一个方法。`.cshtml` 视图与 MCP 工具都调用这一个方法，模板填充逻辑只此一处。

> 注意：除第 2 点的 bug 修正外，重构不改变解析语义（URN 格式 `urn:ddi:{agency}:{identifier}:{version}`，其中 `agency` 同时等于 `AssignmentId` 与 `AgencyId`）。

## 4. 共享校验（Web 与 MCP 共用）

`request_agency` 的机构 ID/标签校验必须与 Web 端**完全一致**，避免审批前写入无效记录。因此把 Web 现有的校验逻辑抽取为 `Ddi.Registry.Data` 中的共享校验器 `AgencyIdValidator`，由 Web 的 `ManageController.AddAgency` 与 MCP 的 `request_agency` 同时调用：

- 必填：`AgencyId` 与 `Label` 均不能为空。
- 格式：复用 `AgencyModel.AgencyId` 的正则 `[a-zA-Z]{2,3}\.[a-zA-Z0-9](-?[a-zA-Z0-9]+)*` 且长度 ≤ 50（该正则已天然排除 `us.a.b` 之类嵌套点号）。
- 国家码约定：前缀必须为 2 或 3 字符；2 字符时须为 `uk` 或**真实有效的 ISO 3166 alpha-2**（经 `NISOCountries.Ripe` 查表校验），3 字符时须为 `int`。这部分依赖 `NISOCountries.Core` / `NISOCountries.Ripe` 包与 `iso3166-countrycodes.txt` 数据文件——文件作为 `Ddi.Registry.Data` 的单一内容文件复制到应用输出目录，校验器以 `Path.Combine(AppContext.BaseDirectory, "iso3166-countrycodes.txt")` 传给 `RipeISOCountryReader.Parse(string path)`。**ISO 数据文件缺失或读取异常时，校验回退为仅格式通过**（有意增强，避免因文件部署问题阻塞所有请求；后续 Web 改用共享校验器后统一获得该行为）。不得把 `StreamReader` 传给该 API。

## 5. 四个工具的定义

| 工具 | 类型 | 行为 |
|---|---|---|
| `resolve_urn(urn)` | 只读 | `DdiUrn.TryParse` 解析 URN（拒绝非 DDI scheme）→ 按 `ddiurn.Agency` 查找 `Assignment`（即 `AssignmentId`）及其所属 `Agency`，并强制 `Agency.ApprovalState == Approved` → 加载其 `HttpResolvers` → 返回 `{ agencyId, agencyLabel?, endpoints:[{resolutionType, url}] }`。即使异常数据或已 Deprecated 机构仍保留 assignment，也不得解析。查不到时返回清晰的「未找到」说明。 |
| `list_agencies(country?)` | 只读 | 当传入 `country` 时，按 `AgencyId.StartsWith((country + ".").ToLowerInvariant())` 前缀过滤；返回**所有**审批状态（Requested / Approved / Deprecated / None）；按 `AgencyId` 排序；投影为 `{agencyId, label, approvalState, dateCreated, dateApproved}`。 |
| `get_services(assignmentId)` | 只读 | 包装 `RegistryProvider.GetServicesForAssignment` → 返回 `{serviceId, hostname, port, serviceName, protocol, priority, weight, timeToLive}`。 |
| `request_agency(label, org)` | 受控写入 | `org` 为建议的 `AgencyId`，**经 `AgencyIdValidator` 完整校验**（见第 4 节，与 Web 一致）。若已存在则拒绝。调用方身份从**已校验的外部 IdP 令牌**声明（`email` 优先，回退 `sub`）映射到**已存在的** `AspNetUsers` 行，作为 `CreatorId=AdminContactId=TechnicalContactId`。无法映射（用户不存在）时以清晰错误失败（受外键约束）。**不发送邮件**。返回 `{agencyId, approvalState, message}`。 |

每个工具都通过构造注入 `ApplicationDbContext`，并使用 `[McpServerTool]`（`[McpServerToolType]` 聚合）注解，附带英文 `Title`/`Description` 与参数说明。

## 6. 配置、身份映射与安全

- `appsettings.json`：`ConnectionStrings:DefaultConnection`（与 Web 相同的 PG 连接串）+ `MCP:Oidc:Authority`（外部 IdP 的 issuer/base URL，**不能**填写 `/.well-known/openid-configuration`）+ `MCP:Oidc:Audience`（启用认证时必填的 resource audience）+ `MCP:Oidc:Scopes`（空白分隔的 `ddi.registry.read ddi.registry.write`）。

### 调用方身份映射（必须处理）
`request_agency` 先以已校验 IdP 令牌的 `email`（或 `ClaimTypes.Email`）归一化后匹配 `NormalizedEmail`；该查询未命中时，再以 `sub` 匹配 `AspNetUsers.Id`。映射完成后才检查机构 ID 是否重复，避免未映射调用方枚举已有 ID。`Agency.CreatorId` 是到 `AspNetUsers` 的外键，因此映射失败（用户不存在）时以清晰错误失败。Web 应用的现有本地账户是身份来源。

### 远程认证：OAuth 2.0 受保护资源（Protected Resource）
为满足「标准远程客户端（如 Claude Desktop）可自动完成认证并调用」的成功标准，MCP 端点配置为 **OAuth 2.0 受保护资源**：

- 使用 SDK 2.0.0 的认证扩展接线：`AddAuthentication(options => { options.DefaultAuthenticateScheme = "Bearer"; options.DefaultChallengeScheme = "McpAuth"; })` 后追加 `.AddJwtBearer("Bearer", …).AddMcp(…)`。Bearer 只负责令牌认证，`McpAuth` 负责 **401 challenge** 与受保护资源 metadata（默认路径 `/.well-known/oauth-protected-resource/mcp`），标准 MCP 客户端能据此自动发现授权服务器并完成授权码流程、拿到 Bearer token。授权策略不得固定认证 scheme 为 `Bearer`，否则 challenge 会绕过 `McpAuth`。
- `ResourceMetadata` 描述的是 MCP 服务自身这个受保护资源，其中 `AuthorizationServers` 指向外部 IdP 的 issuer/base URL，且必须发布配置中的 `ddi.registry.read` / `ddi.registry.write` supported scopes。`ResourceMetadataUri` 若显式设置，也只能指向该受保护资源 metadata，不能指向 IdP 的 OpenID configuration；本期使用 SDK 的默认 `/.well-known/oauth-protected-resource/mcp` 地址。
- **不**手写 `/.well-known/oauth-authorization-server` 路由或重定向；授权服务器的 metadata 由配置的外部 IdP 原生提供。
- Authority 已配置时，只对 `/mcp` endpoint builder 施加不指定 scheme 的 `RequireAuthenticatedUser()` 授权策略；未携带有效令牌即由 `McpAuth` 返回含 protected-resource metadata 的 401。实现和测试必须断言 `WWW-Authenticate` 的 metadata 参数，并确认 metadata 端点匿名可达；scope 的细粒度校验在工具内完成。
- `Development` 环境可在 Authority 未设置时匿名本地运行；其他环境缺少 Authority 或 Audience 必须启动失败，不能静默公开 MCP 端点。

### AWS ALB 后的转发头
经 AWS 负载均衡（HTTPS 终止 + 可能改写 Host）部署时，必须配置 `ForwardedHeadersOptions`（转发 `X-Forwarded-For` / `X-Forwarded-Proto` / `X-Forwarded-Host`）并在管道早期 `UseForwardedHeaders()`，否则 OAuth / protected-resource metadata 会生成错误的 `http://` 地址。只可配置 ALB 所在私网 CIDR 或固定反向代理为 `KnownNetworks` / `KnownProxies`；不得清空两者来信任任意来源的转发头。

> 范围说明：本次仅 MCP 侧做令牌校验与 OAuth 资源服务器配置；Web 应用保留其现有本地 Identity（Cookie / 表单登录）不变。

## 7. 涉及的文件

### 新建
- `src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
- `src/Ddi.Registry.Mcp/Program.cs`
- `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- `src/Ddi.Registry.Mcp/appsettings.json`
- `src/Ddi.Registry.Mcp/appsettings.Development.json`
- `src/Ddi.Registry.Data/AgencyIdValidator.cs` —— 共享机构 ID/标签校验（含 ISO 3166）。
- `src/Ddi.Registry.Data/iso3166-countrycodes.txt` —— 从 Web 项目复制，作为唯一的运行期内容文件并复制到应用输出目录（供 `NISOCountries.Ripe.Parse(path)` 解析）。
- `src/Ddi.Registry.Mcp.Tests/`（xUnit）
  - `Ddi.Registry.Mcp.Tests.csproj`
  - `DdiUrnTests.cs` / `HttpResolverResolveUrlTests.cs`（无 DB）
  - `McpWebApplicationFactory.cs` / `TestAuthHandler.cs`（集成测试宿主 + 测试认证）
  - `ToolIntegrationTests.cs`（工具发现、401、身份映射、创建、重复、审批后 resolve）

### 修改
- `src/Ddi.Registry.Data/DdiUrn.cs` —— 从 Web 移入；修正 scheme 判断为 `||`。
- `src/Ddi.Registry.Data/HttpResolver.cs` —— 新增 `ResolveUrl(DdiUrn)`。
- `src/Ddi.Registry.Data/Ddi.Registry.Data.csproj` —— 新增 `NISOCountries.Core` / `NISOCountries.Ripe`（1.2.0）及 ISO 文件的 `CopyToOutputDirectory` 配置。
- `src/Ddi.Registry.Web/Models/DdiUrn.cs` —— 删除，改为引用 `Ddi.Registry.Data.DdiUrn`。
- `src/Ddi.Registry.Web/Models/ManageModels.cs`、`Controllers/AgencyController.cs` —— 更新 `using`（引用移入 Data 的 `DdiUrn`）。
- `src/Ddi.Registry.Web/Views/Agency/Resolver.cshtml` —— 改用 `HttpResolver.ResolveUrl(...)`。
- `src/Ddi.Registry.Web/Controllers/ManageController.cs` —— `AddAgency` 改为调用共享 `AgencyIdValidator`（移除内联的国家码/ISO 校验块）。
- `Ddi.Registry.Web.sln` —— 加入新项目。
- （可选）`docker-compose.yml` —— 增加 MCP 服务条目。

## 8. 实现时的注意点

`ModelContextProtocol` 固定 **2.0.0**。执行 `dotnet add package ModelContextProtocol --version 2.0.0`（及 `ModelContextProtocol.AspNetCore --version 2.0.0`）后，先在还原出的包（`~/.nuget` 中的 XML/文档）上核对公开 API 面，再接线，并固定一个兼容 net10 的版本号：

- 服务器注册入口为 `IServiceCollection.AddMcpServer()`（非 `AddMcp()`——后者在 2.0.0 是认证扩展），链式 `WithHttpTransport()`（即 Streamable HTTP）+ `WithTools<RegistryTools>()`（或 `WithToolsFromAssembly`）完成工具发现；**不要**只靠 `AddScoped<RegistryTools>()`。
- `MapMcp("/mcp")`、`McpServerTool` / `McpServerToolType` 特性命名空间以实际为准（预期 `ModelContextProtocol.AspNetCore` / `ModelContextProtocol.Server`）。
- OAuth 资源服务器相关扩展（若 2.0.0 提供，如受保护资源元数据 / `WithOAuthAuthorization` 之类）以实际包为准，并与本文第 6 节的 ASP.NET Core 标准 JWT + metadata 端点方案互补。

## 9. 成功标准

- 四个 MCP 工具通过 Streamable HTTP 端点（`/mcp`）可用，标准 MCP 客户端（Claude Desktop / 远程客户端）能经 OAuth 自动发现并完成认证后调用。
- 工具发现返回恰好四个工具：`resolve_urn`、`list_agencies`、`get_services`、`request_agency`。
- `resolve_urn` 对真实已审批 URN 返回正确的填充后 URL；对非 DDI scheme / 不存在的 URN 返回清晰错误（且不接受 `urn:not-ddi:...`）。
- `list_agencies` 支持国家前缀过滤，并返回所有审批状态。
- `get_services` 返回某 assignment 的全部服务记录。
- `request_agency` 以 `Requested` 状态正确插入机构；ID/标签经共享校验器校验（与 Web 一致）；重复 `org` 被拒；IdP 令牌身份无法映射到本地用户时给出清晰报错。
- 启用认证后，未携带有效令牌的 `/mcp` 请求返回 401；protected-resource metadata 端点匿名可访问。
- 集成测试覆盖：工具发现、认证开启后的 401 及 protected-resource metadata challenge、read/write scope 拒绝、令牌身份映射失败、合法请求创建 Requested、重复 ID、审批后 `resolve_urn` 的端到端行为。PostgreSQL 容器测试并发写入，验证唯一约束 `23505` 被转换为重复 ID 结果。
- 重构后，Web 应用原有解析/审批行为保持不变（`.cshtml` 改用共享 `ResolveUrl`；`AddAgency` 改用共享 `AgencyIdValidator`）。

## 10. 风险与未决项

- **外部 IdP 身份映射**：调用方需拥有与本地 `AspNetUsers` 邮箱一致的 IdP 账户，否则 `request_agency` 映射失败。需确认部署时该映射关系已就绪（或提供说明文档）。
- **OAuth 资源服务器细节**：`McpAuth` challenge 与 Bearer authentication 必须分工配置；metadata 端点的 issuer/audience 必须配合 ALB 转发头，避免生成错误地址。
- **MCP 包版本与 API 面**：固定 2.0.0；以还原包实际公开 API 为准，不臆测符号名。
- **数据库写入并发/幂等**：`request_agency` 对重复 `org` 直接拒绝；同时捕获且仅转换 `Agencies` 主键导致的 PostgreSQL `23505`，保证并发重复请求得到相同结果。其他唯一约束异常必须保留为服务端错误并记录。
