# DDI 注册表 MCP Server 设计文档

- **日期**：2026-08-02
- **状态**：已确认设计，待评审
- **范围**：将 DDI Agency Registry 的查询与（受限的）提交能力，封装为基于 Model Context Protocol (MCP) 的远程工具服务。

## 1. 目标与范围

把 `Ddi.Registry.Data` 中现有的查询能力以 MCP 工具的形式暴露出来，供 LLM/智能体在远程（SSE/HTTP）方式下调用。本期包含四个工具，其中三个只读、一个受控写入：

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
使用 `ModelContextProtocol` 的 ASP.NET Core 集成（大致为 `AddMcp().WithHttpTransport()` 配合 `MapMcp("/mcp")`），以 SSE/HTTP 形式对外暴露，部署在现有 AWS 负载均衡（HTTPS 终止）之后。

- MCP 服务**不运行 EF 迁移**：数据库 Schema 与迁移由 Web 应用负责；MCP 仅连接到一个已被迁移过的数据库。

## 3. 必要的重构（避免 Web 与 MCP 两份实现）

为保证单一真相源，先做两处小重构：

1. **移动 `DdiUrn`**：从 `Ddi.Registry.Web.Models` 移动到 `Ddi.Registry.Data`（它是领域类型；MCP 不应引入整个 Web/Identity 技术栈）。同步修改 Web 中各处的命名空间引用。
2. **新增 `HttpResolver.ResolveUrl(DdiUrn urn)`**：把目前硬编码在 `Resolver.cshtml:31-35` 的四次 `Replace("{agency}" / "{identifier}" / "{version}" / "{urn}")` 逻辑，封装为 `Ddi.Registry.Data` 中的一个方法。`.cshtml` 视图与 MCP 工具都调用这一个方法，模板填充逻辑只此一处。

> 注意：重构仅移动/抽取，不改变解析语义（URN 格式 `urn:ddi:{agency}:{identifier}:{version}`，其中 `agency` 同时等于 `AssignmentId` 与 `AgencyId`）。

## 4. 四个工具的定义

| 工具 | 类型 | 行为 |
|---|---|---|
| `resolve_urn(urn)` | 只读 | `DdiUrn.TryParse` 解析 URN → 按 `ddiurn.Agency` 查找 `Assignment`（即 `AssignmentId`）→ 加载其 `HttpResolvers` → 返回 `{ agencyId, agencyLabel?, endpoints:[{resolutionType, url}] }`。本质上只对**已审批（Approved）**的机构有效（因为 `Assignment` 行在审批通过时创建，见 `AdminController.Approve`）。查不到时返回清晰的「未找到」说明。 |
| `list_agencies(country?)` | 只读 | 当传入 `country` 时，按 `AgencyId.StartsWith((country + ".").ToLowerInvariant())` 前缀过滤；返回**所有**审批状态（Requested / Approved / Deprecated / None，依用户选择）；按 `AgencyId` 排序；投影为 `{agencyId, label, approvalState, dateCreated, dateApproved}`。 |
| `get_services(assignmentId)` | 只读 | 包装 `RegistryProvider.GetServicesForAssignment` → 返回 `{serviceId, hostname, port, serviceName, protocol, priority, weight, timeToLive}`。 |
| `request_agency(label, org)` | 受控写入 | `org` 为建议的 `AgencyId`，按 `country.name` 格式做轻量校验（必须包含 `.`，前缀 2 或 3 字符，复用现有校验逻辑）。若已存在则拒绝。插入 `Agency { AgencyId=org, Label=label, ApprovalState=Requested, CreatorId=AdminContactId=TechnicalContactId=由环境变量配置的 service-account 用户 id }`。**不发送邮件**。返回 `{agencyId, approvalState, message}`。 |

每个工具都通过构造注入 `ApplicationDbContext`，并使用 `[McpServerTool]`（`[McpServerToolType]` 聚合）注解，附带中文 `Title`/`Description` 与参数说明。

## 5. 配置与一个硬约束

- `appsettings.json`：`ConnectionStrings:DefaultConnection`（与 Web 相同的 PG 连接串）+ `MCP:ServiceAccountUserId`。
- **外键约束（必须处理）**：`Agency.CreatorId` 是到 `AspNetUsers` 表的外键。因此 service-account id 必须指向一个**已存在的用户行**。工具在 id 不存在时，以清晰的错误信息失败，并记录需预先存在该服务账户用户（可手动 seed，或复用某个已有 admin 的 id）。

## 6. 安全（推荐，非 MVP 阻塞项）

一个对外暴露「写入」工具的远程端点，可能被滥用来刷审批队列。计划内置一个**可选的 Bearer Token 中间件**，由环境变量 `MCP:AUTHORIZATION_TOKEN` 控制：未设置时为空操作（不校验）；设置后对所有 MCP 请求校验 `Authorization: Bearer <token>`。默认关闭，设置该环境变量即开启。MVP 阶段可仅通过网络安全策略（如仅内网/VPC）限制访问。

## 7. 涉及的文件

### 新建
- `src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj`
- `src/Ddi.Registry.Mcp/Program.cs`
- `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- `src/Ddi.Registry.Mcp/appsettings.json`
- `src/Ddi.Registry.Mcp/appsettings.Development.json`
- `src/Ddi.Registry.Mcp.Tests/`（xUnit：覆盖 `ResolveUrl` 与 `DdiUrn.TryParse`，无需数据库）
  - `Ddi.Registry.Mcp.Tests.csproj`
  - `HttpResolverResolveUrlTests.cs`
  - `DdiUrnTests.cs`

### 修改
- `src/Ddi.Registry.Data/HttpResolver.cs` —— 新增 `ResolveUrl(DdiUrn)` 方法。
- `src/Ddi.Registry.Data/DdiUrn.cs` —— 从 Web 项目移入（原位于 `src/Ddi.Registry.Web/Models/DdiUrn.cs`）。
- `src/Ddi.Registry.Web/Models/DdiUrn.cs` —— 删除，改为引用 `Ddi.Registry.Data.DdiUrn`。
- `src/Ddi.Registry.Web/Views/Agency/Resolver.cshtml` —— 改用 `HttpResolver.ResolveUrl(...)` 替代内联 `Replace` 链。
- `src/Ddi.Registry.Web` 中引用旧命名空间 `DdiUrn` 的代码（`AgencyController` 等）—— 更新 `using`。
- `Ddi.Registry.Web.sln` —— 将新项目加入解决方案。
- （可选）`docker-compose.yml` —— 增加 MCP 服务条目。

## 8. 实现时的注意点

`ModelContextProtocol` 预览版之间，`AddMcp` / `MapMcp` / `WithHttpTransport` 等符号名称可能略有差异。执行 `dotnet add package` 后，应先在还原出的包（`~/.nuget` 中的 XML/文档）上核对公开 API 面，再接线，并固定一个兼容 net10 的版本号。

## 9. 成功标准

- 四个 MCP 工具通过 SSE/HTTP 端点可用，能被标准 MCP 客户端（如 Claude Desktop / 远程客户端）发现并调用。
- `resolve_urn` 对真实已审批 URN 返回正确的填充后 URL；对不存在的 URN 返回清晰错误。
- `list_agencies` 支持国家前缀过滤，并返回所有审批状态。
- `get_services` 返回某 assignment 的全部服务记录。
- `request_agency` 以 `Requested` 状态正确插入机构，且 service-account 外键约束在 id 不存在时给出清晰报错。
- 重构后，Web 应用原有解析/审批行为保持不变（`.cshtml` 改用共享 `ResolveUrl`）。
- 单元测试无需数据库即可覆盖模板填充与 URN 解析。

## 10. 风险与未决项

- **service-account 用户**：需确认部署环境已存在该用户，或提供 seed 脚本/步骤。
- **MCP 包版本与 API 面**：以还原包实际公开 API 为准，不臆测符号名。
- **数据库写入并发/幂等**：`request_agency` 对重复 `org` 直接拒绝，本期不处理并发竞态（审批系统本身为低频人工流程）。
