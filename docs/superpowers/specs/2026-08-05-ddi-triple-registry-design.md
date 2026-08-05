# DDI Triple Registry Design

Date: 2026-08-05
Status: Draft for user review
Scope: Entity model + MCP + Web for Concept/Variable/Representation registration

## 1. Goals

Build complete DDI triple registration capabilities on top of the existing registry model.

- Add first-class registries for Concept, Representation, and Variable.
- Keep existing Agency/Assignment/Service/HttpResolver behavior unchanged.
- Support registration workflow from request to approval/deprecation.
- Enforce cross-entity consistency for publishability.
- Expose capabilities through both MCP tools and Web management UI.

## 2. Confirmed Product Decisions

### 2.1 Delivery scope

In scope:
- Data model and database migration.
- MCP read/write/approval tools.
- Web management and approval pages.
- Tests across data, MCP, and Web layers.

### 2.2 Reference and publishability rules

- Variable creation may reference `Requested` Concept/Representation entities.
- A Variable is publishable only if all three are `Approved`:
  - Variable itself
  - Referenced Concept
  - Referenced Representation
- Cross-agency references are denied by default.

### 2.3 IRDI strategy

- Default path: system-generated IRDI from `AgencyId + Name + Version`.
- Admin import path: allow full IRDI input.
- Both paths must pass format, ownership, and uniqueness checks.

Canonical formats:
- Concept: `urn:irdi:{agency}:concept:{name}:{version}`
- Variable: `urn:irdi:{agency}:variable:{name}:{version}`
- Representation: `urn:irdi:{agency}:representation:{name}:{version}`

### 2.4 Related concepts model

Use a dedicated relation table (not JSON list).

- Default: relation targets must be same agency.
- Admin override: explicit external or cross-agency relation is allowed and audited.

### 2.5 Approval model

- Creator can only create/update request-stage records.
- Only existing `admin`/`SuperAdmin` can approve/deprecate.
- Current enum has no `Rejected`; this release does not add a new approval enum value.

### 2.6 JSON Schema/SHACL boundary

Chosen implementation level:
- Validate `JsonSchema` as valid JSON payload and basic structural constraints.
- Validate Variable->Representation reference integrity.
- Persist `ShaclTemplateIrdi` as validated reference only.
- No RDF/SHACL execution engine in this release.

## 3. Alternative Approaches Considered

### A. Independent entities + strong relation tables (recommended)

- Three dedicated registry tables + one concept relation table.
- Strong FK and index strategy.
- Predictable query and approval behavior.

Pros:
- Aligns with existing EF-style domain model.
- Clear constraints and easier approval logic.
- Better MCP/Web implementation clarity.

Cons:
- More schema objects and migration code.

### B. Single generic registration table with JSON payload

Pros:
- Faster schema bootstrap.

Cons:
- Weak compile-time integrity.
- Harder joins, constraints, approval queries, and form validation.

### C. Hybrid (core columns + flexible metadata JSON)

Pros:
- More extensible metadata model.

Cons:
- Higher complexity in v1 without clear immediate benefit.

Decision: Approach A.

## 4. Target Domain Model

### 4.1 New entities

`ConceptRegistration`
- `Id: Guid`
- `Irdi: string` (unique)
- `AgencyId: string` (FK -> Agency)
- `Name: string`
- `Version: string`
- `Label: string`
- `Definition: string`
- `DomainOntology: string` (e.g., `GoodCrew`/`TokenHub`)
- `MapsToClass: string` (e.g., `gc:DigitalWorker`)
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`RepresentationRegistration`
- `Id: Guid`
- `Irdi: string` (unique)
- `AgencyId: string` (FK -> Agency)
- `Name: string`
- `Version: string`
- `Type: string` (`Numeric`/`Text`/`Code`/`DateTime`)
- `JsonSchema: string`
- `ShaclTemplateIrdi: string`
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`VariableRegistration`
- `Id: Guid`
- `Irdi: string` (unique)
- `AgencyId: string` (FK -> Agency)
- `Name: string`
- `Version: string`
- `ConceptIrdi: string` (FK -> ConceptRegistration.Irdi)
- `RepresentationIrdi: string` (FK -> RepresentationRegistration.Irdi)
- `SourceType: string` (`Survey`/`API`/`OCR`/`SystemLog`)
- `CollectionMethod: string`
- `Universe: string`
- `QualityGate: string` (`Block`/`Warn`/`Off`)
- `ApprovalState: ApprovalState`
- `CreatedAt: DateTime`
- `UpdatedAt: DateTime?`

`ConceptRelation`
- `Id: Guid`
- `SourceConceptIrdi: string` (FK -> ConceptRegistration.Irdi)
- `TargetConceptIrdi: string` (nullable when external only)
- `TargetExternalIrdi: string` (nullable)
- `IsCrossAgency: bool`
- `CreatedByUserId: string`
- `CreatedAt: DateTime`

### 4.2 Constraints and indexes

- Unique `Irdi` in each registry table.
- Unique `(AgencyId, Name, Version)` in each registry table.
- Indexes for `AgencyId`, `ApprovalState`, `CreatedAt`.
- Variable FK references enforced via `ConceptIrdi` and `RepresentationIrdi`.
- Validation-level constraint: Variable, Concept, Representation must share `AgencyId` unless explicit admin override policy allows otherwise (override only for related concept links, not variable foreign references).

## 5. Data Flow and State Flow

### 5.1 Creation flow

1. Authenticate and authorize.
2. Map caller identity to existing local user.
3. Validate input format.
4. Resolve IRDI (generated or imported).
5. Validate ownership and uniqueness.
6. Validate references (for Variable and Concept relations).
7. Persist record with `ApprovalState.Requested`.

### 5.2 Approval flow

- Admin/SuperAdmin actions:
  - `approve_*`: set `ApprovalState.Approved`
  - `deprecate_*`: set `ApprovalState.Deprecated`
- Non-admin approval attempts are rejected.

### 5.3 Publishability flow

- Derived field `IsPublishable` for variable queries:
  - True only when variable + concept + representation are all approved.
  - Never directly editable.

## 6. MCP Tool Design

### 6.1 Read (`ddi.registry.read`)

- `list_concepts`
- `get_concept`
- `list_representations`
- `get_representation`
- `list_variables`
- `get_variable`
- `get_variable_publishability`

### 6.2 Write (`ddi.registry.write`)

- `request_concept`
- `request_representation`
- `request_variable`
- `update_concept_request`
- `update_representation_request`
- `update_variable_request`
- `link_related_concept`

### 6.3 Admin approval

- `approve_concept`, `approve_representation`, `approve_variable`
- `deprecate_concept`, `deprecate_representation`, `deprecate_variable`

### 6.4 Error semantics

Follow existing MCP message style:
- `Missing required scope '...'`
- `Caller identity could not be mapped...`
- `... already exists.`
- Deterministic messages for invalid references and cross-agency violations.

## 7. Web Design

Add pages under management/admin flows:

- User-facing management:
  - Concept requests
  - Representation requests
  - Variable requests
- Admin approval queues:
  - Requested Concept list
  - Requested Representation list
  - Requested Variable list

UI behavior:
- Request-stage records are editable by creators (policy-aligned).
- Approved/Deprecated records become read-only for non-admin.
- Variable detail page shows publishability and reason when not publishable.

## 8. Validation Rules

- IRDI format + uniqueness + ownership checks.
- Enumerated field checks (`DomainOntology`, `Type`, `SourceType`, `QualityGate`).
- `JsonSchema` must be valid JSON and pass basic structural checks.
- `ShaclTemplateIrdi` format validation only.
- Variable references must exist and obey agency policy.
- Concept relation policy:
  - default same-agency only
  - admin explicit override required for cross-agency/external.

## 9. Testing Strategy

### 9.1 Data tests

- IRDI generation/import validation.
- Unique constraints and conflict translation.
- Publishability logic.
- Agency boundary checks.

### 9.2 MCP integration tests

- Scope boundaries for read/write/admin.
- Identity mapping precedence before duplicate checks.
- Variable with requested references (allowed but not publishable).
- Cross-agency relation restrictions and admin override path.

### 9.3 Web integration tests

- Management CRUD in request stage.
- Admin approval/deprecation operations.
- Validation error rendering and consistency.

### 9.4 Regression tests

- No behavior regression in existing Agency/Assignment/Service/Resolver flows.

## 10. Non-Goals (This Release)

- RDF graph ingestion and SHACL execution.
- JSON schema instance validation engine.
- Approval role model redesign.

## 11. Rollout Notes

- Add migration with additive tables/indexes only.
- Keep old endpoints and data untouched.
- Enable new MCP/Web capabilities behind normal auth and role checks.
- Document IRDI generation/import behavior in README and operator docs.
