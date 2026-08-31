# DDI 三元组注册能力 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 DDI Registry 中新增 Concept/Representation/Variable 三元组注册表，提供完整的申请、审批、查询与发布性判定能力，并通过 MCP 与 Web 同步暴露。

**Architecture:** 采用“独立实体 + 强关系表”方案：在 `Ddi.Registry.Data` 增加三类注册实体与概念关联实体，使用外键、唯一索引与服务层校验共同保证一致性。MCP 在 `RegistryTools` 上新增读写审批工具并保持现有错误语义；Web 在 `Manage`/`Admin` 扩展对应页面与审批流程。Variable 的可发布性由查询层派生字段统一计算，避免前端重复逻辑。

**Tech Stack:** .NET 10、ASP.NET Core MVC + Identity、EF Core + Npgsql、ModelContextProtocol 2.0.0、xUnit 2.9.2。

## Global Constraints

- 交付范围必须包含：数据模型与迁移、MCP 工具、Web 管理与审批、分层测试。
- Variable 创建时允许引用 `Requested` 的 Concept/Representation，但发布要求三者均为 `Approved`。
- 默认禁止跨 Agency 引用；仅 Concept 关系允许管理员显式建立跨 Agency/外部 IRDI 关联并审计。
- IRDI 默认系统按 `AgencyId + Name + Version` 生成，同时允许管理员导入完整 IRDI；两路径都做格式、归属、唯一性校验。
- 审批仅 `admin`/`SuperAdmin` 可执行，状态使用现有 `ApprovalState`（`None/Requested/Approved/Deprecated`），本期不新增 `Rejected`。
- `JsonSchema` 仅做合法 JSON 与基础结构校验；`ShaclTemplateIrdi` 仅保存与校验引用，不引入 SHACL 执行引擎。
- 不改变现有 Agency/Assignment/Service/HttpResolver 对外行为。

## Design Review Decisions (Focused)

### IA-1 入口与信息架构（已确认）

- 决策：采用混合入口。
  - `Manage`：提供三元组注册总览、快速新建入口、个人待处理项。
  - `ViewAgency`：提供该 Agency 下 Concept/Representation/Variable 的详情、状态与可发布性解释。
- 目标：同时满足“跨实体快速操作”与“按 Agency 归属追踪”的两类使用场景。
- 实施约束：
  - 不新增独立顶级导航，保持现有站点导航稳定。
  - 所有详情页必须显示 `AgencyId` 与 `ApprovalState`，避免跨 Agency 语义混淆。
  - Variable 详情页必须显示 `IsPublishable` 及阻塞原因（缺失批准链）。

### UX-STATE-1 交互状态模型（已确认）

- 决策：采用完整状态模型（`Loading/Empty/Error/Success/Partial`）。
- 覆盖流程：
  - 提交 Concept。
  - 创建 Representation。
  - 绑定 Variable（含 RelatedConcepts 管理）。
  - 审批动作（Approve/Reject）。
- 必须满足：
  - `Loading`：表单提交按钮禁用，显示进度状态，防止重复提交。
  - `Empty`：列表空态必须给出下一步行动（如“去创建 Concept”）。
  - `Error`：字段级校验错误 + 顶部汇总错误，区分可修复错误与权限/策略错误。
  - `Success`：返回可操作后续（如“继续创建 Representation”或“查看详情”）。
  - `Partial`：用于 Variable 可发布性解释，明确展示“已满足项/阻塞项”。
- 后端契约要求：
  - API/MCP 返回错误需可映射为上述状态，不允许只返回模糊失败。
  - 对 `Forbidden`、`Validation`、`Conflict` 三类错误使用稳定错误码，便于 UI 精准渲染。

### RWD-A11Y-1 响应式与可访问性基线（已确认）

- 决策：采用完整基线（桌面/平板/手机三断点 + 可访问性基础合规）。
- 断点与布局：
  - 桌面（`>=1200px`）：三栏信息布局（列表/详情/操作区）可用。
  - 平板（`768px-1199px`）：两栏布局（列表+详情），操作区折叠为抽屉或分段。
  - 手机（`<768px`）：单栏堆叠，关键操作固定在可见区域（底部操作条或顶部主按钮）。
- 可访问性最低要求：
  - 所有表单控件具备可关联 `label` 与错误提示关联（`aria-describedby`）。
  - 键盘可达：主要流程（创建、提交审批、审批通过）可在无鼠标下完成。
  - 状态提示可被读屏识别：成功/错误/部分可发布说明使用语义区域（如 `role="status"` / `role="alert"`）。
  - 对比度满足常规文本可读性要求；禁止仅靠颜色表达审批状态。
- 验收补充：
  - 每个核心页面需提供桌面与手机两种截图基准（用于回归对比）。
  - Web 测试至少覆盖：移动端关键按钮可见、错误提示存在、空状态引导存在。

### Focused Review Scorecard（3 维）

- 初始评分：
  - 信息架构 `6/10`
  - 交互状态 `4/10`
  - 响应式与可访问性 `3/10`
- 当前决策落地后预估评分：
  - 信息架构 `8/10`
  - 交互状态 `8/10`
  - 响应式与可访问性 `7/10`
- 到 `10/10` 仍缺项：
  - 在 Task 级别补充页面清单与逐页状态矩阵（每页的 `Loading/Empty/Error/Success/Partial`）。
  - 为 `Manage` 与 `ViewAgency` 列出字段优先级与信息密度规则（移动端优先展示字段）。
  - 增加可访问性自动化检查步骤（最小化回归风险）。

### UI-Page Inventory（执行清单）

- `Manage/TripleRegistryOverview`：总览页（我的待处理、最近变更、快速入口）。
- `Manage/CreateConcept`：创建 Concept 表单页。
- `Manage/CreateRepresentation`：创建 Representation 表单页。
- `Manage/CreateVariable`：创建 Variable 表单页（含 Concept/Representation 绑定）。
- `Manage/VariableDetails`：Variable 详情页（含 `IsPublishable` 与阻塞原因）。
- `ViewAgency/TripleRegistryTab`：按 Agency 视角查看三元组状态与详情。
- `Admin/TripleRegistryApprovals`：审批列表页（支持筛选与批量操作入口）。

### UI-State Matrix（逐页五态）

- `Manage/TripleRegistryOverview`
  - `Loading`：骨架屏 + 禁用快速入口按钮。
  - `Empty`：无数据时展示“从创建 Concept 开始”引导。
  - `Error`：展示加载失败与重试按钮。
  - `Success`：展示摘要卡片、待处理项与最近变更。
  - `Partial`：数据部分加载失败时仅降级相关卡片，不阻断整页。
- `Manage/CreateConcept`
  - `Loading`：提交中按钮禁用并显示进度。
  - `Empty`：首次进入显示字段说明与命名建议。
  - `Error`：字段级错误 + 顶部错误摘要（冲突/权限/格式）。
  - `Success`：创建成功并提供“继续创建 Representation”。
  - `Partial`：自动填充失败时允许手动补录。
- `Manage/CreateRepresentation`
  - `Loading`：提交中禁用重复提交。
  - `Empty`：无可用 Concept 时引导先创建或选择已有 Concept。
  - `Error`：`JsonSchema` 非法与引用错误分开提示。
  - `Success`：成功后提供“继续绑定 Variable”。
  - `Partial`：Schema 校验通过但外部引用未解析时允许保存为 Requested。
- `Manage/CreateVariable`
  - `Loading`：提交中禁用引用选择器。
  - `Empty`：缺 Concept/Representation 数据时显示前置操作链接。
  - `Error`：跨 Agency 违规、唯一冲突、字段错误分层提示。
  - `Success`：成功后跳转详情并高亮可发布性状态。
  - `Partial`：允许创建但展示“未满足发布条件”清单。
- `Manage/VariableDetails`
  - `Loading`：详情与依赖链分区骨架屏。
  - `Empty`：实体不存在或无权限时显示安全空态。
  - `Error`：加载失败显示重试与支持信息。
  - `Success`：显示 `IsPublishable`、阻塞项、审批链。
  - `Partial`：部分依赖缺失时保留主体信息并标红阻塞项。
- `Admin/TripleRegistryApprovals`
  - `Loading`：审批列表与筛选面板骨架。
  - `Empty`：无待审批项时显示“当前已清空”。
  - `Error`：审批失败需回显失败项与原因。
  - `Success`：审批成功后列表即时更新并提示影响实体。
  - `Partial`：批量审批部分成功时展示成功/失败明细。

### Mobile Field Priority（移动端字段优先级）

- `Concept`
  - 一级：`Name`、`Version`、`ApprovalState`。
  - 二级：`AgencyId`、`Description`。
  - 三级：`CreatedAt`、`CreatedBy`、审计附加信息。
- `Representation`
  - 一级：`Name`、`Version`、`ConceptIrdi`、`ApprovalState`。
  - 二级：`JsonSchema` 摘要（折叠显示）。
  - 三级：`ShaclTemplateIrdi`、审计字段。
- `Variable`
  - 一级：`Name`、`Version`、`IsPublishable`、`ApprovalState`。
  - 二级：`ConceptIrdi`、`RepresentationIrdi`。
  - 三级：`RelatedConcepts` 展开区、审计字段。
- 审批列表
  - 一级：实体类型、名称、当前状态、申请人。
  - 二级：申请时间、Agency。
  - 三级：扩展原因与系统审计信息。

### Accessibility Automation（自动化检查步骤）

- 新增前端自动化测试目标：
  - 表单控件存在可访问名称（label 或 aria-label）。
  - 错误提示通过 `aria-describedby` 与控件关联。
  - 成功/失败/部分状态区域具备语义角色。
  - 键盘 Tab 顺序可完成核心流程。
- 建议实现方式（本仓库可行最小集）：
  - 在 `src/Ddi.Registry.Web.Tests` 新增可访问性集成测试，使用页面 HTML 断言关键属性存在。
  - 对关键页面至少增加 1 个“键盘路径可达”测试场景（创建 Concept 与审批通过）。
- 验收门禁：
  - PR 必须通过新增的 Web 可访问性测试用例。
  - 任一核心页面缺失语义状态提示则阻止合并。

### Low-Fidelity Wireframe & Component Checklist（低保真线框与组件清单）

- `Manage/TripleRegistryOverview`
  - 区块 A：页头（标题、说明、主操作按钮“新建 Concept”）。
  - 区块 B：摘要卡（待审批数量、可发布变量数量、阻塞变量数量）。
  - 区块 C：最近变更列表（实体类型、名称、状态、更新时间）。
  - 区块 D：我的待处理（审批任务或补全任务）。
- `Manage/CreateConcept`
  - 区块 A：基础字段（Name、Version、Description、Agency）。
  - 区块 B：IRDI 预览（系统生成/管理员导入切换）。
  - 区块 C：校验反馈区（字段级 + 顶部摘要）。
  - 区块 D：提交操作区（保存草稿、提交审批）。
- `Manage/CreateRepresentation`
  - 区块 A：基础字段与 Concept 关联选择。
  - 区块 B：JsonSchema 编辑区（文本域 + 语法状态）。
  - 区块 C：ShaclTemplateIrdi 引用输入区。
  - 区块 D：提交与后续动作区。
- `Manage/CreateVariable`
  - 区块 A：基础字段（Name、Version、Agency）。
  - 区块 B：Concept/Representation 绑定区。
  - 区块 C：RelatedConcepts 管理区（添加、移除、冲突提示）。
  - 区块 D：可发布性预检区（满足项/阻塞项实时展示）。
  - 区块 E：提交与跳转区（创建后去详情）。
- `Manage/VariableDetails`
  - 区块 A：主信息卡（状态、IsPublishable、版本）。
  - 区块 B：依赖链卡（Concept/Representation/RelatedConcepts）。
  - 区块 C：阻塞原因卡（不可发布原因及下一步引导）。
  - 区块 D：审计时间线（创建、更新、审批事件）。
- `Admin/TripleRegistryApprovals`
  - 区块 A：筛选栏（实体类型、Agency、状态、申请人、时间）。
  - 区块 B：审批列表（可批量选择）。
  - 区块 C：批量操作条（Approve、Deprecated、取消）。
  - 区块 D：结果回执抽屉（批量结果明细）。

### Batch Approval Partial-Failure Copy Strategy（批量审批部分失败文案策略）

- 场景定义：一次批量审批中，部分记录成功，部分因冲突/权限/状态变化失败。
- 反馈结构（固定顺序）：
  - 顶部摘要：`本次共处理 N 条，成功 S 条，失败 F 条`。
  - 失败分组：按 `Conflict`、`Forbidden`、`Validation` 三类分组展示。
  - 单条明细：显示实体名、失败原因、建议动作。
  - 可恢复操作：`仅重试失败项`、`导出失败清单`、`返回筛选`。
- 文案模板：
  - Conflict：`该记录状态已变化，请刷新后重试。`
  - Forbidden：`你没有审批该记录的权限，请联系管理员。`
  - Validation：`记录不满足发布前置条件，请先完成依赖项。`
- 交互规则：
  - 成功项立即从待审批列表移除，失败项保留并高亮。
  - 失败项默认展开前 5 条，支持“查看全部”。
  - 重试操作仅针对失败项，不重复提交成功项。
- 审计要求：
  - 批量审批结果写入审计日志，包含请求人、时间、成功/失败 ID 列表。
  - UI 回执中的失败原因必须与后端错误码一一对应。

### Task Adjustments（计划补充）

- 在 Web 任务中追加子步骤：
  - 先实现页面骨架与状态区域，再接入数据绑定。
  - 为每个页面对照 `UI-State Matrix` 打钩自检。
  - 按 `Mobile Field Priority` 调整移动端首屏信息密度。
  - 按 `Low-Fidelity Wireframe & Component Checklist` 对每页做区块对齐，不允许随意删减关键区块。
  - `Admin/TripleRegistryApprovals` 必须实现 `Batch Approval Partial-Failure Copy Strategy` 中定义的回执结构。
- 在测试任务中追加子步骤：
  - 新增 `TripleRegistryAccessibilityTests`（或等价命名）并纳入常规测试命令。
  - 新增批量审批部分失败 UI/接口协同测试，断言摘要、分组和重试行为。

## GSTACK REVIEW REPORT

- Review Mode: Focused 3 passes（信息架构 + 交互状态 + 响应式/可访问性）
- Decision Log:
  - IA: 选择混合入口（`Manage` 总览 + `ViewAgency` 详情）
  - UX States: 选择完整五态模型（`Loading/Empty/Error/Success/Partial`）
  - RWD/A11Y: 选择完整三断点 + 可访问性基线
- Risk Before Review:
  - 页面职责边界不清，可能造成重复实现与返工
  - 状态反馈不足，审批与发布阻塞原因难以解释
  - 移动端与读屏路径未定义，验收风险高
- Improvements Applied:
  - 增加可执行页面清单与逐页五态矩阵
  - 增加移动端字段优先级规则，控制信息密度
  - 增加可访问性自动化门禁，降低回归风险
- Final Focused Score:
  - 信息架构：`10/10`
  - 交互状态：`10/10`
  - 响应式与可访问性：`10/10`
- Remaining Gaps to 10/10:
  - 无（已在本计划内补齐页面区块、批量失败文案与自动化门禁）。

---

## Parallel Execution Streams（并行执行工作流）

### Stream A: Data Model & Migration

- 范围：Task 1 + Task 2 + 数据层校验与迁移。
- 主要产出：实体、DbContext 映射、迁移脚本、数据层测试。
- 前置依赖：无（最先启动）。
- 完成定义：
  - `Ddi.Registry.Data` 编译通过。
  - 数据层相关测试通过。
  - 迁移仅新增三元组相关对象，无破坏性变更。

### Stream B: MCP Tools

- 范围：`RegistryTools` 三元组工具新增与错误语义对齐。
- 主要产出：读写审批工具、错误码映射、MCP 集成测试。
- 前置依赖：依赖 Stream A 的实体与查询接口稳定。
- 完成定义：
  - MCP 工具列表包含三元组工具。
  - `Forbidden/Validation/Conflict` 错误可被稳定断言。
  - MCP 测试通过且不影响现有工具行为。

### Stream C: Web Manage/Admin UI

- 范围：控制器、模型、视图、状态区域、审批回执。
- 主要产出：混合入口页面、五态反馈、批量审批部分失败回执。
- 前置依赖：
  - 依赖 Stream A 的读写模型与状态计算。
  - 与 Stream B 并行，但错误码契约需同步。
- 完成定义：
  - 页面满足 `UI-State Matrix` 与组件清单。
  - 移动端字段优先级规则落地。
  - 批量审批失败策略全部可见且可交互。

### Stream D: Test & Quality Gate

- 范围：Web/MCP/Data 新增测试、可访问性门禁、回归验证。
- 主要产出：集成测试、可访问性测试、关键回归报告。
- 前置依赖：A/B/C 基本功能可运行。
- 完成定义：
  - 新增测试全部通过。
  - 原有核心测试无回归失败。
  - 验收门禁命令可在本地与 CI 复现。

## Dependency Gates（串行门禁）

- Gate 1（Schema Gate）
  - 条件：Stream A 完成并通过数据层测试。
  - 通过后：允许 Stream B/C 进入功能实现阶段。
- Gate 2（Contract Gate）
  - 条件：Stream B 输出稳定错误码与返回结构。
  - 通过后：Stream C 固化错误展示与回执文案。
- Gate 3（UX Gate）
  - 条件：Stream C 完成五态与移动端规则实现。
  - 通过后：Stream D 执行可访问性与回归全量验证。
- Gate 4（Release Gate）
  - 条件：Stream D 全绿 + 关键手工验收通过。
  - 通过后：更新 README 并准备合并。

## Daily Checkpoints（每日检查点）

- Day 1 目标
  - 完成 Stream A（实体、映射、迁移、基础测试）。
  - 产出：可迁移数据库与通过的数据层测试结果。
- Day 2 目标
  - 完成 Stream B 主要工具与集成测试。
  - 并行启动 Stream C 页面骨架与状态容器。
- Day 3 目标
  - 完成 Stream C 交互细节、批量审批失败回执与移动端适配。
  - 完成 Stream D 可访问性测试与回归测试。
- Day 4 目标（缓冲）
  - 修复回归问题，补文档与验收截图，准备合并。

## Commit Order（建议提交顺序）

1. `feat(data): add triple-registry entities, constraints, and migrations`
2. `feat(mcp): add triple-registry tools with stable error contracts`
3. `feat(web): add manage/admin triple-registry flows and state UI`
4. `test(web): add accessibility and partial-failure approval tests`
5. `docs: update README for triple-registry workflows and constraints`

## Parallelization Notes（并行实施说明）

- 可并行：A 与 C 的页面骨架阶段可并行；B 与 C 在错误码契约确认前需每日同步。
- 不可并行：迁移最终版本与基于迁移的稳定测试，必须在 A 收敛后统一执行。
- 冲突高风险文件：
  - `src/Ddi.Registry.Web/Controllers/ManageController.cs`
  - `src/Ddi.Registry.Web/Controllers/AdminController.cs`
  - `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
  - `src/Ddi.Registry.Data/ApplicationDbContext.cs`
- 协作规则：
  - 每个 stream 在提交前 rebase 到最新 `mcpserver`。
  - 若错误码或字段名变更，必须同步更新 `UI-State Matrix` 对应说明。

## Executable Task Cards（可分配任务卡）

### Card A1: 三元组实体与 IRDI 能力

- Owner: Data Engineer
- Stream: A
- Input:
  - 本计划 Task 1
  - `ApprovalState` 现有语义
- Output:
  - 实体文件：`ConceptRegistration/RepresentationRegistration/VariableRegistration/ConceptRelation`
  - `RegistryIrdi` 生成与解析能力
  - `RegistryIrdiTests` 通过
- Acceptance Commands:
  - `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~RegistryIrdiTests" --no-restore`
- Estimate: 0.5-1 day
- Risk:
  - IRDI 格式边界处理不足导致后续校验不一致

### Card A2: DbContext 约束与迁移

- Owner: Data Engineer
- Stream: A
- Input:
  - Card A1 输出
- Output:
  - `ApplicationDbContext` 新增 `DbSet` 与索引/外键约束
  - `TripleRegistry` 迁移文件与 snapshot 更新
  - Schema 测试通过
- Acceptance Commands:
  - `dotnet ef migrations add TripleRegistry --project src/Ddi.Registry.Data --startup-project src/Ddi.Registry.Web`
  - `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~TripleRegistrySchemaTests" --no-restore`
- Estimate: 0.5-1 day
- Risk:
  - 外键目标与唯一索引设计不一致导致迁移回滚

### Card B1: MCP 三元组读写工具

- Owner: MCP Engineer
- Stream: B
- Input:
  - Card A2 输出
  - 现有 `RegistryTools` 错误语义
- Output:
  - MCP 工具：创建、查询、审批动作与可发布性查询
  - 错误码稳定映射：`Forbidden/Validation/Conflict`
- Acceptance Commands:
  - `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --filter "FullyQualifiedName~TripleRegistryToolIntegrationTests" --no-restore`
- Estimate: 1 day
- Risk:
  - 工具返回结构不稳定导致 Web 端状态难映射

### Card C1: Manage 侧三元组流程页

- Owner: Web Engineer
- Stream: C
- Input:
  - Card A2 输出
  - `UI-State Matrix` + `Low-Fidelity Wireframe & Component Checklist`
- Output:
  - `Manage` 总览/创建/详情页面与状态区
  - 移动端字段优先级落地
- Acceptance Commands:
  - `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryManageTests" --no-restore`
- Estimate: 1-1.5 days
- Risk:
  - 表单状态与后端错误码未对齐，导致 Partial/Error 展示混乱

### Card C2: Admin 审批页与部分失败回执

- Owner: Web Engineer
- Stream: C
- Input:
  - Card B1 输出
  - `Batch Approval Partial-Failure Copy Strategy`
- Output:
  - `Admin/TripleRegistryApprovals` 列表、批量审批、失败回执抽屉
  - 失败重试仅针对失败项
- Acceptance Commands:
  - `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryAdminApprovalTests" --no-restore`
- Estimate: 0.5-1 day
- Risk:
  - 批量审批并发导致状态漂移，回执与真实结果不一致

### Card D1: 可访问性与回归门禁

- Owner: QA Engineer
- Stream: D
- Input:
  - Card C1 + C2 输出
  - `Accessibility Automation` 要求
- Output:
  - `TripleRegistryAccessibilityTests`（或等价）
  - 批量失败场景 UI/接口协同测试
  - 回归结果摘要
- Acceptance Commands:
  - `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryAccessibilityTests|FullyQualifiedName~TripleRegistry" --no-restore`
  - `dotnet test Ddi.Registry.Web.sln --no-restore`
- Estimate: 0.5-1 day
- Risk:
  - 测试覆盖不足导致移动端与无障碍问题后置暴露

### Card DOC1: README 与运维说明更新

- Owner: Tech Lead / DevEx
- Stream: D（收尾）
- Input:
  - A/B/C/D 卡片全部完成
- Output:
  - README 增加三元组创建/审批/发布规则、常见错误、测试命令
- Acceptance Commands:
  - `dotnet test Ddi.Registry.Web.sln --no-restore`
  - 手工核对 README 命令可执行
- Estimate: 0.25 day
- Risk:
  - 文档与实际行为偏离，影响后续维护

## Handoff Protocol（交接协议）

- 每张卡完成后必须附带：
  - 变更文件列表
  - 运行命令与结果摘要
  - 已知限制与后续建议
- 卡片交接格式（固定模板）：
  - `Card`:
  - `Done`:
  - `Evidence`:
  - `Risks`:
  - `Next`:
- Gate 触发规则：
  - A2 完成后触发 Gate 1
  - B1 完成后触发 Gate 2
  - C1+C2 完成后触发 Gate 3
  - D1+DOC1 完成后触发 Gate 4

---

## File Structure

- 数据层新增：
  - `src/Ddi.Registry.Data/ConceptRegistration.cs`
  - `src/Ddi.Registry.Data/RepresentationRegistration.cs`
  - `src/Ddi.Registry.Data/VariableRegistration.cs`
  - `src/Ddi.Registry.Data/ConceptRelation.cs`
  - `src/Ddi.Registry.Data/RegistryIrdi.cs`
  - `src/Ddi.Registry.Data/RegistrationValidation.cs`
- 数据层修改：
  - `src/Ddi.Registry.Data/ApplicationDbContext.cs`
  - `src/Ddi.Registry.Data/Migrations/*`（新增迁移与 snapshot）
- MCP 修改：
  - `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- Web 修改：
  - `src/Ddi.Registry.Web/Controllers/ManageController.cs`
  - `src/Ddi.Registry.Web/Controllers/AdminController.cs`
  - `src/Ddi.Registry.Web/Models/AdminModels.cs`
  - `src/Ddi.Registry.Web/Models/ManageModels.cs`
- Web 视图新增（按现有目录约定）：
  - `src/Ddi.Registry.Web/Views/Manage/*TripleRegistry*.cshtml`
  - `src/Ddi.Registry.Web/Views/Admin/*TripleRegistry*.cshtml`
- MCP 测试新增：
  - `src/Ddi.Registry.Mcp.Tests/TripleRegistryToolIntegrationTests.cs`
- Data/Web 测试新增：
  - `src/Ddi.Registry.Data.Tests/`（若不存在则新建测试项目）
  - `src/Ddi.Registry.Web.Tests/TripleRegistryManageTests.cs`
  - `src/Ddi.Registry.Web.Tests/TripleRegistryAdminApprovalTests.cs`
- 文档更新：
  - `README.md`

### Task 1: 新增领域实体与基础值对象

**Files:**
- Create: `src/Ddi.Registry.Data/ConceptRegistration.cs`
- Create: `src/Ddi.Registry.Data/RepresentationRegistration.cs`
- Create: `src/Ddi.Registry.Data/VariableRegistration.cs`
- Create: `src/Ddi.Registry.Data/ConceptRelation.cs`
- Create: `src/Ddi.Registry.Data/RegistryIrdi.cs`
- Modify: `src/Ddi.Registry.Data/Agency.cs`（如需复用注释与枚举说明，不改枚举值）
- Test: `src/Ddi.Registry.Data.Tests/RegistryIrdiTests.cs`

**Interfaces:**
- Consumes: `ApprovalState`（`Agency.cs`）
- Produces:
  - `static class RegistryIrdi`:
    - `string BuildConceptIrdi(string agencyId, string name, string version)`
    - `string BuildRepresentationIrdi(string agencyId, string name, string version)`
    - `string BuildVariableIrdi(string agencyId, string name, string version)`
    - `bool TryParse(string irdi, out RegistryIrdiParts parts)`
  - `record RegistryIrdiParts(string AgencyId, string Kind, string Name, string Version)`

- [ ] **Step 1: 写失败测试（IRDI 生成与解析）**

```csharp
[Fact]
public void BuildConceptIrdi_ShouldMatchCanonicalFormat()
{
    var irdi = RegistryIrdi.BuildConceptIrdi("us.demo", "worker-status", "1.0");
    Assert.Equal("urn:irdi:us.demo:concept:worker-status:1.0", irdi);
}

[Fact]
public void TryParse_ShouldExtractParts()
{
    var ok = RegistryIrdi.TryParse("urn:irdi:us.demo:variable:employment:1.0", out var parts);
    Assert.True(ok);
    Assert.Equal("us.demo", parts!.AgencyId);
    Assert.Equal("variable", parts.Kind);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~RegistryIrdiTests" --no-restore`

Expected: FAIL，提示 `RegistryIrdi` 未定义。

- [ ] **Step 3: 最小实现 IRDI 工具与实体骨架**

```csharp
public static class RegistryIrdi
{
    public static string BuildConceptIrdi(string agencyId, string name, string version)
        => $"urn:irdi:{agencyId}:concept:{name}:{version}";
}
```

并为四个实体添加属性骨架与默认时间字段（`CreatedAt = DateTime.UtcNow`）。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~RegistryIrdiTests" --no-restore`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Data src/Ddi.Registry.Data.Tests
git commit -m "feat(data): add triple-registry entities and IRDI utility"
```

### Task 2: DbContext 映射、约束与迁移

**Files:**
- Modify: `src/Ddi.Registry.Data/ApplicationDbContext.cs`
- Create: `src/Ddi.Registry.Data/Migrations/*_TripleRegistry.cs`（由 `dotnet ef migrations add TripleRegistry` 生成）
- Modify: `src/Ddi.Registry.Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `src/Ddi.Registry.Data.Tests/TripleRegistrySchemaTests.cs`

**Interfaces:**
- Consumes: Task 1 entity classes.
- Produces:
  - `DbSet<ConceptRegistration> ConceptRegistrations`
  - `DbSet<RepresentationRegistration> RepresentationRegistrations`
  - `DbSet<VariableRegistration> VariableRegistrations`
  - `DbSet<ConceptRelation> ConceptRelations`

- [ ] **Step 1: 写失败测试（唯一索引与外键）**

```csharp
[Fact]
public async Task Variable_ShouldRequireExistingConceptAndRepresentation()
{
    await using var db = CreatePostgresContext();
    db.VariableRegistrations.Add(new VariableRegistration {
        Irdi = "urn:irdi:us.demo:variable:v:1",
        AgencyId = "us.demo",
        ConceptIrdi = "urn:irdi:us.demo:concept:c:1",
        RepresentationIrdi = "urn:irdi:us.demo:representation:r:1"
    });

    await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~TripleRegistrySchemaTests" --no-restore`

Expected: FAIL（DbSet/映射不存在）。

- [ ] **Step 3: 在 DbContext 添加映射**

在 `OnModelCreating` 中加入：

```csharp
builder.Entity<ConceptRegistration>()
    .HasIndex(x => x.Irdi)
    .IsUnique();

builder.Entity<ConceptRegistration>()
    .HasIndex(x => new { x.AgencyId, x.Name, x.Version })
    .IsUnique();
```

并对 `RepresentationRegistration`、`VariableRegistration` 复制相同唯一策略；对 Variable 配置两条外键到 `Irdi`。

- [ ] **Step 4: 生成迁移并检查 SQL**

Run: `dotnet ef migrations add TripleRegistry --project src/Ddi.Registry.Data --startup-project src/Ddi.Registry.Web`

Expected: 生成新增表、唯一索引与外键，旧表无破坏性变更。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --no-restore`

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/Ddi.Registry.Data
git commit -m "feat(data): add triple-registry schema and migration"
```

### Task 3: 业务校验服务（同 Agency、导入策略、发布性判定）

**Files:**
- Create: `src/Ddi.Registry.Data/RegistrationValidation.cs`
- Create: `src/Ddi.Registry.Data/VariablePublishability.cs`
- Test: `src/Ddi.Registry.Data.Tests/RegistrationValidationTests.cs`

**Interfaces:**
- Produces:
  - `RegistrationValidationResult ValidateVariableReferences(...)`
  - `bool IsVariablePublishable(ApprovalState variable, ApprovalState concept, ApprovalState representation)`

- [ ] **Step 1: 写失败测试（允许 Requested 引用但不可发布）**

```csharp
[Fact]
public void RequestedReferences_AreAllowed_ButNotPublishable()
{
    var valid = RegistrationValidation.IsVariablePublishable(
        ApprovalState.Requested, ApprovalState.Approved, ApprovalState.Approved);

    Assert.False(valid);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~RegistrationValidationTests" --no-restore`

Expected: FAIL（方法不存在）。

- [ ] **Step 3: 实现最小校验逻辑**

```csharp
public static bool IsVariablePublishable(ApprovalState variable, ApprovalState concept, ApprovalState representation)
    => variable == ApprovalState.Approved
       && concept == ApprovalState.Approved
       && representation == ApprovalState.Approved;
```

并实现同 Agency 默认禁止跨 Agency 引用校验（管理员覆盖仅限 ConceptRelation）。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Data.Tests/Ddi.Registry.Data.Tests.csproj --filter "FullyQualifiedName~RegistrationValidationTests" --no-restore`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Data src/Ddi.Registry.Data.Tests
git commit -m "feat(data): add triple-registry validation and publishability rules"
```

### Task 4: MCP 读取工具（三表查询 + 发布性）

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- Test: `src/Ddi.Registry.Mcp.Tests/TripleRegistryToolIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2 DbSet + Task 3 publishability helper.
- Produces MCP tools:
  - `list_concepts`, `get_concept`
  - `list_representations`, `get_representation`
  - `list_variables`, `get_variable`, `get_variable_publishability`

- [ ] **Step 1: 写失败测试（read scope 可列出 Concept）**

```csharp
[Fact]
public async Task ListConcepts_WithReadScope_ReturnsRecords()
{
    using var factory = new McpWebApplicationFactory();
    factory.Seed();

    var client = await McpHttpTestClient.ConnectAsync(factory, "read");
    var result = await client.CallToolAsync("list_concepts", new Dictionary<string, object>());
    Assert.False(result.IsError);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --filter "FullyQualifiedName~TripleRegistryToolIntegrationTests" --no-restore`

Expected: FAIL（工具不存在）。

- [ ] **Step 3: 实现最小读取工具**

按 `list_agencies/get_services` 现有模式实现：
- 先 `HasScope("ddi.registry.read")`
- 再查询 DbContext
- 返回稳定结果 DTO 与错误消息。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --filter "FullyQualifiedName~TripleRegistryToolIntegrationTests" --no-restore`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs src/Ddi.Registry.Mcp.Tests/TripleRegistryToolIntegrationTests.cs
git commit -m "feat(mcp): add triple-registry read tools"
```

### Task 5: MCP 写入与审批工具（身份优先与稳定错误语义）

**Files:**
- Modify: `src/Ddi.Registry.Mcp/Tools/RegistryTools.cs`
- Test: `src/Ddi.Registry.Mcp.Tests/TripleRegistryToolIntegrationTests.cs`

**Interfaces:**
- Produces MCP tools:
  - `request_concept`, `request_representation`, `request_variable`
  - `update_concept_request`, `update_representation_request`, `update_variable_request`
  - `approve_*`, `deprecate_*`
  - `link_related_concept`

- [ ] **Step 1: 写失败测试（身份映射优先于重复检查）**

```csharp
[Fact]
public async Task RequestConcept_UnknownIdentity_ReturnsMappedIdentityErrorBeforeDuplicateSignal()
{
    using var factory = new McpWebApplicationFactory();
    factory.Seed();

    var client = await McpHttpTestClient.ConnectAsync(factory, "unknown");
    var result = await client.CallToolAsync("request_concept", new Dictionary<string, object>
    {
        ["agencyId"] = "us.seeded",
        ["name"] = "worker-status",
        ["version"] = "1.0",
        ["label"] = "Worker Status"
    });

    Assert.Contains("could not be mapped", result.Content);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --filter "FullyQualifiedName~RequestConcept_UnknownIdentity" --no-restore`

Expected: FAIL（工具/语义不存在）。

- [ ] **Step 3: 实现写入与审批工具**

实现要点：
- 顺序：scope -> identity mapping -> 格式校验 -> 引用/归属校验 -> 唯一性。
- `request_*` 固定写入 `ApprovalState.Requested`。
- 审批工具校验管理员角色。
- 并发唯一冲突转换为 `... already exists.`。

- [ ] **Step 4: 运行 MCP 全量测试**

Run: `dotnet test src/Ddi.Registry.Mcp.Tests/Ddi.Registry.Mcp.Tests.csproj --no-restore`

Expected: PASS（含现有 Keycloak OIDC 测试）。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Mcp/Tools/RegistryTools.cs src/Ddi.Registry.Mcp.Tests/TripleRegistryToolIntegrationTests.cs
git commit -m "feat(mcp): add triple-registry write and approval tools"
```

### Task 6: Web 管理流程（申请阶段 CRUD）

**Files:**
- Modify: `src/Ddi.Registry.Web/Controllers/ManageController.cs`
- Modify: `src/Ddi.Registry.Web/Models/ManageModels.cs`
- Create: `src/Ddi.Registry.Web/Views/Manage/AddConceptRegistration.cshtml`
- Create: `src/Ddi.Registry.Web/Views/Manage/EditConceptRegistration.cshtml`
- Create: `src/Ddi.Registry.Web/Views/Manage/AddRepresentationRegistration.cshtml`
- Create: `src/Ddi.Registry.Web/Views/Manage/EditRepresentationRegistration.cshtml`
- Create: `src/Ddi.Registry.Web/Views/Manage/AddVariableRegistration.cshtml`
- Create: `src/Ddi.Registry.Web/Views/Manage/EditVariableRegistration.cshtml`
- Test: `src/Ddi.Registry.Web.Tests/TripleRegistryManageTests.cs`

**Interfaces:**
- Consumes: `_context.ManagesAgency(...)` 授权模式。
- Produces:
  - `Manage/AddConceptRegistration`
  - `Manage/EditConceptRegistration`
  - `Manage/AddRepresentationRegistration`
  - `Manage/EditRepresentationRegistration`
  - `Manage/AddVariableRegistration`
  - `Manage/EditVariableRegistration`

- [ ] **Step 1: 写失败测试（普通用户可创建 Requested 记录）**

```csharp
[Fact]
public async Task AddConceptRegistration_ShouldCreateRequestedRecord()
{
    await using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
    var client = factory.CreateClient();

    var response = await client.PostAsync("/Manage/AddConceptRegistration", Form(new Dictionary<string, string>
    {
        ["AgencyId"] = "us.demo",
        ["Name"] = "worker-status",
        ["Version"] = "1.0",
        ["Label"] = "Worker Status"
    }));

    Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryManageTests" --no-restore`

Expected: FAIL（路由与模型未实现）。

- [ ] **Step 3: 实现申请阶段页面与控制器动作**

按 `AddAgency/EditAgency` 现有风格实现：
- 校验 Agency 管理权限。
- 校验输入与 IRDI 规则。
- 固定写入 `Requested`。
- 编辑仅允许申请阶段记录。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryManageTests" --no-restore`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Web src/Ddi.Registry.Web.Tests
git commit -m "feat(web): add manage flows for triple-registry requests"
```

### Task 7: Web 审批流程（管理员队列与状态转换）

**Files:**
- Modify: `src/Ddi.Registry.Web/Controllers/AdminController.cs`
- Modify: `src/Ddi.Registry.Web/Models/AdminModels.cs`
- Create: `src/Ddi.Registry.Web/Views/Admin/TripleRegistryIndex.cshtml`
- Test: `src/Ddi.Registry.Web.Tests/TripleRegistryAdminApprovalTests.cs`

**Interfaces:**
- Produces:
  - `Admin/TripleRegistryIndex`
  - `Admin/ApproveConceptRegistration`
  - `Admin/ApproveRepresentationRegistration`
  - `Admin/ApproveVariableRegistration`
  - `Admin/DeprecateConceptRegistration`
  - `Admin/DeprecateRepresentationRegistration`
  - `Admin/DeprecateVariableRegistration`

- [ ] **Step 1: 写失败测试（非管理员审批被拒绝）**

```csharp
[Fact]
public async Task ApproveConceptRegistration_NonAdmin_ShouldForbid()
{
    await using var factory = new WebOidcApplicationFactory(configureKeycloak: false);
    var client = factory.CreateClient();

    var response = await client.GetAsync("/Admin/ApproveConceptRegistration?id=00000000-0000-0000-0000-000000000001");
    Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryAdminApprovalTests" --no-restore`

Expected: FAIL（动作不存在）。

- [ ] **Step 3: 实现管理员审批动作与队列页面**

遵循现有 `AdminController`：
- `[Authorize(Roles = "admin,SuperAdmin")]`
- Requested 列表查询。
- 批准设置 `Approved`，废弃设置 `Deprecated`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test src/Ddi.Registry.Web.Tests/Ddi.Registry.Web.Tests.csproj --filter "FullyQualifiedName~TripleRegistryAdminApprovalTests" --no-restore`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/Ddi.Registry.Web src/Ddi.Registry.Web.Tests
git commit -m "feat(web): add admin approval flows for triple-registry"
```

### Task 8: 端到端回归、文档与清理

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-05-ddi-triple-registry-design.md`（仅在实现与规格偏差时更新）

**Interfaces:**
- Consumes: Task 1-7 的完整功能。
- Produces: 可复现验证命令与使用说明。

- [ ] **Step 1: 写失败检查（README 缺少三元组能力说明）**

Run: `rg "ConceptRegistration|RepresentationRegistration|VariableRegistration|publishable" README.md`

Expected: 无匹配或内容不完整。

- [ ] **Step 2: 更新 README**

补充：
- 新增实体与 IRDI 规则。
- 发布性判定定义。
- MCP 工具列表（读/写/审批）。
- Web 入口路径（Manage/Admin）。

- [ ] **Step 3: 执行全量验证**

Run: `dotnet test Ddi.Registry.Web.sln --no-restore`
Expected: 全部 PASS。

Run: `docker compose up --build -d; docker compose ps; docker compose down -v --remove-orphans`
Expected: 关键服务可启动，环境可清理。

- [ ] **Step 4: 最终提交**

```bash
git add README.md src docs
git commit -m "feat: deliver DDI triple registry across data mcp and web"
```

## Plan Self-Review

- Spec coverage:
  - 数据模型与迁移：Task 1-2 覆盖。
  - 业务规则与发布性：Task 3 覆盖。
  - MCP 读写审批：Task 4-5 覆盖。
  - Web 管理与审批：Task 6-7 覆盖。
  - 测试与文档：Task 8 覆盖。
- Placeholder scan:
  - 无 `TBD/TODO/implement later` 文案。
- Type consistency:
  - IRDI 工具、可发布性接口、MCP 工具名在任务间一致。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-05-ddi-triple-registry.md`. Two execution options:

1. Subagent-Driven (recommended) - I dispatch a fresh subagent per task, review between tasks, fast iteration
2. Inline Execution - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
