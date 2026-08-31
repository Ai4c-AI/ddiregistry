# DDI 三元组注册表设计

日期：2026-08-05  
状态：待用户审阅  
范围：为 Concept/Variable/Representation 提供实体模型 + MCP + Web 注册能力

## 1. 目标

在现有注册表模型基础上，构建完整的 DDI 三元组注册能力。

- 新增 Concept、Representation、Variable 三类一等注册表。
- 保持现有 Agency/Assignment/Service/HttpResolver 行为不变。
- 支持从申请到审批/废弃的完整生命周期。
- 对发布能力施加跨实体一致性约束。
- 同时通过 MCP 工具和 Web 管理界面提供能力。

## 2. 已确认产品决策

### 2.1 交付范围

本期包含：
- 数据模型与数据库迁移。
- MCP 读写与审批工具。
- Web 管理与审批页面。
- 数据层、MCP 层、Web 层测试。

### 2.2 引用与可发布规则

- Variable 创建时允许引用处于 `Requested` 的 Concept/Representation。
- Variable 仅在以下三者均为 `Approved` 时才可发布：
  - Variable 自身
  - 引用的 Concept
  - 引用的 Representation
- 默认禁止跨 Agency 引用。

### 2.3 IRDI 策略

- 默认路径：系统根据 `AgencyId + Name + Version` 生成 IRDI。
- 管理员导入路径：允许提交完整 IRDI。
- 两条路径都必须通过格式、归属和唯一性校验。

规范格式：
- Concept：`urn:irdi:{agency}:concept:{name}:{version}`
- Variable：`urn:irdi:{agency}:variable:{name}:{version}`
- Representation：`urn:irdi:{agency}:representation:{name}:{version}`

### 2.4 RelatedConcepts 建模

采用独立关系表，不使用 JSON 字符串数组。

- 默认要求关系目标属于同一 Agency。
- 管理员可显式建立外部 IRDI 或跨 Agency 关联，并写入审计信息。

### 2.5 审批模型

- 创建者仅可创建/更新申请阶段记录。
- 仅现有 `admin`/`SuperAdmin` 可执行审批与废弃。
- 当前枚举不存在 `Rejected`；本期不新增审批状态枚举值。

### 2.6 JSON Schema 与 SHACL 边界

本期能力边界：
- 校验 `JsonSchema` 为合法 JSON 且满足基础结构约束。
- 校验 Variable -> Representation 引用完整性。
- `ShaclTemplateIrdi` 仅作为受校验的引用字段持久化。
- 不引入 RDF/SHACL 执行引擎。

## 3. 备选方案与取舍

### A. 独立实体 + 强关系表（推荐）

- 三张注册实体表 + 一张概念关系表。
- 使用强外键和索引约束。
- 查询与审批行为可预测。

优点：
- 与现有 EF 风格一致。
- 约束边界清晰，审批逻辑简单。
- MCP/Web 实现更直观。

缺点：
- 数据库对象与迁移代码更多。

### B. 单一通用注册表 + JSON 载荷

优点：
- 初始建表更快。

缺点：
- 编译期约束弱。
- Join、约束、审批查询和表单校验复杂。

### C. 混合方案（核心列 + 扩展 JSON）

优点：
- 元数据扩展弹性更好。

缺点：
- v1 复杂度更高，当前收益不明确。

结论：采用方案 A。

## 4. 目标领域模型

### 4.1 新增实体

`ConceptRegistration`
- `Id: Guid`
- `Irdi: string`（唯一）
- `AgencyId: string`（FK -> Agency）
- `Name: string`
- `Version: string`
- `Label: string`
- `Definition: string`
- `DomainOntology: string`（如 `GoodCrew`/`TokenHub`）
- `MapsToClass: string`（如 `gc:DigitalWorker`）
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`RepresentationRegistration`
- `Id: Guid`
- `Irdi: string`（唯一）
- `AgencyId: string`（FK -> Agency）
- `Name: string`
- `Version: string`
- `Type: string`（`Numeric`/`Text`/`Code`/`DateTime`）
- `JsonSchema: string`
- `ShaclTemplateIrdi: string`
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`VariableRegistration`
- `Id: Guid`
- `Irdi: string`（唯一）
- `AgencyId: string`（FK -> Agency）
- `Name: string`
- `Version: string`
- `ConceptIrdi: string`（FK -> ConceptRegistration.Irdi）
- `RepresentationIrdi: string`（FK -> RepresentationRegistration.Irdi）
- `SourceType: string`（`Survey`/`API`/`OCR`/`SystemLog`）
- `CollectionMethod: string`
- `Universe: string`
- `QualityGate: string`（`Block`/`Warn`/`Off`）
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`ConceptRelation`
- `Id: Guid`
- `SourceConceptIrdi: string`（FK -> ConceptRegistration.Irdi）
- `TargetConceptIrdi: string`（可空，仅当目标为内部联系时使用）
- `TargetExternalIrdi: string`（可空）
- `IsCrossAgency: bool`
- `CreatedByUserId: string`
- `CreatedAt: DateTime`

### 4.2 约束与索引

- 每张注册表内 `Irdi` 唯一。
- 每张注册表内 `(AgencyId, Name, Version)` 唯一。
- 为 `AgencyId`、`ApprovalState`、`CreatedAt` 建索引。
- Variable 通过 `ConceptIrdi` 与 `RepresentationIrdi` 建立外键引用。
- 规则层约束：Variable、Concept、Representation 必须同 Agency；仅 ConceptRelation 允许管理员显式跨 Agency 或外部 IRDI 关联。

## 5. 数据流与状态流

### 5.1 创建流程

1. 认证与授权。
2. 将调用者映射到本地用户。
3. 校验输入格式。
4. 生成或解析 IRDI。
5. 校验归属与唯一性。
6. 校验引用关系（Variable 与 Concept 关联场景）。
7. 以 `ApprovalState.Requested` 持久化。

### 5.2 审批流程

- `admin`/`SuperAdmin` 可执行：
  - `approve_*`：设置为 `ApprovalState.Approved`
  - `deprecate_*`：设置为 `ApprovalState.Deprecated`
- 非管理员审批请求直接拒绝。

### 5.3 可发布判定

- 对 Variable 提供派生字段 `IsPublishable`：
  - 仅当 Variable、Concept、Representation 全为 Approved 时为真。
  - 该字段不可直接编辑。

## 6. MCP 工具设计

### 6.1 读取工具（`ddi.registry.read`）

- `list_concepts`
- `get_concept`
- `list_representations`
- `get_representation`
- `list_variables`
- `get_variable`
- `get_variable_publishability`

### 6.2 写入工具（`ddi.registry.write`）

- `request_concept`
- `request_representation`
- `request_variable`
- `update_concept_request`
- `update_representation_request`
- `update_variable_request`
- `link_related_concept`

### 6.3 管理员审批工具

- `approve_concept`, `approve_representation`, `approve_variable`
- `deprecate_concept`, `deprecate_representation`, `deprecate_variable`

### 6.4 错误语义

沿用现有 MCP 消息风格：
- `Missing required scope '...'`
- `Caller identity could not be mapped...`
- `... already exists.`
- 对非法引用与跨 Agency 违规返回稳定、可预期错误文本。

## 7. Web 设计

在现有管理/管理员流程下新增页面：

- 用户管理页：
  - Concept 申请
  - Representation 申请
  - Variable 申请
- 管理员审批队列：
  - Concept Requested 列表
  - Representation Requested 列表
  - Variable Requested 列表

界面行为：
- 申请阶段记录可由创建者编辑（符合权限策略）。
- Approved/Deprecated 记录对非管理员只读。
- Variable 详情页显示可发布状态及不可发布原因。

## 8. 校验规则

- IRDI 格式、唯一性、归属校验。
- 枚举字段校验（`DomainOntology`、`Type`、`SourceType`、`QualityGate`）。
- `JsonSchema` 必须为合法 JSON 且满足基础结构要求。
- `ShaclTemplateIrdi` 仅做格式/引用校验。
- Variable 引用必须存在并满足 Agency 规则。
- ConceptRelation 规则：
  - 默认仅同 Agency
  - 跨 Agency 或外部关联必须由管理员显式执行。

## 9. 测试策略

### 9.1 数据层测试

- IRDI 生成与导入校验。
- 唯一约束与冲突错误翻译。
- 可发布逻辑校验。
- Agency 边界校验。

### 9.2 MCP 集成测试

- read/write/admin scope 边界。
- 身份映射优先于重复检查（防枚举）。
- Variable 引用 Requested 实体（允许创建但不可发布）。
- 跨 Agency 关系限制与管理员覆盖路径。

### 9.3 Web 集成测试

- 申请阶段管理 CRUD。
- 管理员审批/废弃操作。
- 校验错误渲染与一致性。

### 9.4 回归测试

- 现有 Agency/Assignment/Service/Resolver 功能无行为回归。

## 10. 非目标（本期不做）

- RDF 图加载与 SHACL 执行。
- JSON Schema 实例数据验证引擎。
- 审批角色体系重构。

## 11. 发布与上线说明

- 迁移采用增量表/索引方式，不破坏旧表。
- 保持旧接口与历史数据不变。
- 新 MCP/Web 能力受现有认证与角色控制。
- 在 README 与运维文档中补充 IRDI 生成/导入规则。
