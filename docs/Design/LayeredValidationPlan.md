# MCP Validator Layered Validation Plan

## Intent

This document defines the next design step for MCP Validator after the current category-based validator model.

The target state is an evidence-first validation engine that:

- keeps neutral MCP evidence separate from host-specific compatibility judgments
- distinguishes MCP specification requirements from guidance and heuristics
- adds first-class workflow and scenario validation instead of relying on one-shot probes
- exposes coverage and blind spots explicitly in every report format
- remains reusable across future hosts, not only the CLI

This document should be read together with `ValidationExtensibilityBlueprint.md` and `ValidationMigrationMap.md` for the implementation-grade contracts and the first concrete migration steps.

This plan builds on the repository boundaries already documented in `Architecture.md`, `TechnicalArchitecture.md`, `ComponentDesign.md`, and `ForwardArchitecturePlan.md`.

## Why This Is Needed

The current engine already has several good foundations:

- structured neutral findings in `Mcp.Benchmark.Core/Models/ValidationFinding.cs`
- finding rollups in `Mcp.Benchmark.Core/Services/ValidationFindingAggregator.cs`
- host-level gate logic in `Mcp.Benchmark.Core/Services/ValidationPolicyEvaluator.cs`
- separate client-profile interpretation in `Mcp.Benchmark.ClientProfiles`
- report rendering isolated in `Mcp.Benchmark.Infrastructure/Services/Reporting`

The main limitation is not lack of structure. It is that the runtime model is still primarily category-oriented:

- protocol
- tools
- resources
- prompts
- security
- performance

That shape works for structural checks, but it is too shallow for:

- error hygiene versus error usefulness
- eventual consistency and multi-step workflows
- output usability and pagination semantics
- explicit coverage declarations
- progressively disclosed reports

The gap observed in the Dynamics/Claude comparison came from exactly this boundary: the repo already catches several static metadata and schema issues, but it does not yet model workflow behavior and output quality as first-class validation layers.

## External Grounding

### MCP Specification and Official Docs

The official MCP docs and spec establish the protocol floor, not the full product-quality bar.

Key points that directly matter to validator design:

- MCP is explicitly split into a data layer and a transport layer.
- All implementations must support JSON-RPC and lifecycle management.
- Initialization must be the first interaction.
- The client sends `initialize`, the server responds with negotiated protocol version, capabilities, and `serverInfo`, and the client then sends `notifications/initialized`.
- On HTTP transports, negotiated protocol version must be carried forward via the MCP protocol header on subsequent requests.
- Tools are discovered via `tools/list` and executed via `tools/call`.
- Tool `inputSchema` must be a valid JSON Schema object. For parameterless tools, empty-object schemas are valid and `additionalProperties: false` is the recommended stricter form.
- Tool `outputSchema` is optional, but when present the server must return `structuredContent` that conforms to it, and clients should validate it.
- Tool list change notifications are first-class protocol notifications when `tools.listChanged` is declared.
- Tool annotations are hints, not guarantees, and clients must treat them as untrusted unless the server itself is trusted.
- The annotation defaults are intentionally pessimistic: missing annotations imply non-read-only, potentially destructive, non-idempotent, and open-world behavior.
- Authorization is optional at the protocol level. HTTP implementations should follow the MCP authorization guidance; STDIO implementations should not try to reuse HTTP authorization semantics.
- Tasks are now part of the evolving protocol surface and matter for long-running or deferred workflows.

Design implication: the validator must continue to mark true protocol violations as `spec`, but must not overstate host UX preferences or best-practice guidance as core MCP conformance.

### Host Documentation

Official host documentation adds a second authority layer: documented client behavior.

Claude Code guidance currently emphasizes:

- HTTP as the preferred remote transport
- SSE as deprecated
- support for dynamic `list_changed` updates
- tool search and concise server instructions for tool discoverability
- output-size handling, including `anthropic/maxResultSizeChars`
- OAuth metadata discovery and scope restriction controls
- managed allow/deny policy for approved MCP servers

VS Code documentation currently emphasizes:

- explicit trust prompts before starting servers
- sandboxing for local stdio servers on macOS and Linux
- automatic approval only inside sandboxed conditions
- support for tools, resources, prompts, and apps
- debugging and operational visibility through MCP server logs

GitHub Copilot documentation currently splits across multiple surfaces:

- GitHub Copilot Cloud Agent is tools-only
- Cloud Agent does not support resources or prompts
- Cloud Agent does not currently support remote MCP servers that rely on OAuth
- Cloud Agent uses configured tools autonomously and does not ask for approval before tool use
- GitHub recommends allowlisting only the necessary tools, especially read-only tools
- Copilot CLI and IDE surfaces support explicit server configuration and tool selection
- IDE-based flows still include user trust or approval moments depending on environment

Design implication: client profiles must remain additive and careful. Host docs should influence profile judgments, not rewrite neutral evidence. A host-specific incompatibility is not the same thing as an MCP spec failure.

### Research and Adjacent Papers

There is not yet a mature MCP-specific academic literature that can serve as the sole source of validation rules. The best design input comes from:

- official MCP specification and maintainers' docs
- official host documentation
- adjacent tool-use and agent-reliability research

The adjacent work is still useful if translated carefully:

`ReAct`

- Main lesson: tool quality is trajectory-level, not just call-level.
- Translation into validation: add scenario validators that test preconditions, action sequences, failure recovery, and postconditions.

`Gorilla` and APIBench

- Main lesson: API success depends heavily on schema clarity, argument specificity, and documentation freshness.
- Translation into validation: go beyond surface JSON Schema validity and score argument disambiguation quality, format constraints, enum completeness, output structure, and tool naming clarity.

`Scaling Laws for Reward Model Overoptimization`

- Main lesson: optimized proxy scores diverge from true quality if the proxy becomes the target.
- Translation into validation: never let one scalar trust score become the system's truth. Keep raw evidence, per-layer outcomes, and policy signals first-class.

This research should influence architecture and rule design, not be reported as if it were normative MCP authority.

## Current Repository Seams

The plan should preserve the current separation of concerns.

### Core

Already owns the right foundation for:

- validation contracts and models
- structured findings and rule IDs
- finding aggregation and policy signal construction
- neutral result persistence

### Infrastructure

Already owns the right foundation for:

- transport and session bootstrap
- auth discovery and auth flows
- protocol and primitive validators
- trust scoring and report generation

### Client Profiles

Already owns the right foundation for:

- host-specific requirement interpretation
- documented compatibility summaries
- coverage-aware requirement summaries

### Reporting

Already renders from saved results, which is the correct product shape.

The problem is that HTML and markdown are still partially composing report structure directly from category result objects. The next step is to give them a shared section tree so they stop diverging.

## Target Architecture

### 1. Keep `ValidationResult` as the aggregate root, but not as a god object

Replace the current result model in one deliberate cutover.

Instead, keep `ValidationResult` as the persisted envelope and move new first-class structures into dedicated subdocuments:

```csharp
public sealed class ValidationResult
{
    public ValidationRunDocument Run { get; init; } = new();
    public ValidationAssessmentDocument Assessments { get; init; } = new();
    public ValidationEvidenceDocument Evidence { get; init; } = new();
    public ValidationCompatibilityDocument Compatibility { get; init; } = new();
}
```

Then place the new ledgers under those documents:

- `Assessments.Layers`
- `Assessments.Scenarios`
- `Evidence.Observations`
- `Evidence.Coverage`

The existing category properties should be removed once the new subdocuments and their consumers are in place.

Important guardrail:

- do not keep temporary top-level mirrors in the final state
- the target state should not keep adding new first-class lists directly onto `ValidationResult`
- reports should continue to be derived from `ValidationResult`, not stored as the source of truth

### 2. Make layers first-class

The validator should not flatten everything into categories. It should evaluate explicit layers.

Proposed layers:

| Layer | Purpose | Primary owner today | Main addition needed |
| --- | --- | --- | --- |
| `bootstrap` | Reachability, transport startup, handshake prerequisites, auth discovery | `ValidationSessionBuilder` | Explicit coverage and deferred/blocked outcomes |
| `transport-auth` | HTTP or stdio transport behavior, auth challenge shape, protocol headers, auth boundary behavior | auth/session/bootstrap + auth validators | clearer distinction between transport conformance and host auth compatibility |
| `protocol-core` | JSON-RPC, lifecycle, negotiated capabilities, notifications | `ProtocolComplianceValidator` | richer version/capability evidence and notification behavior |
| `primitive-contracts` | tools/resources/prompts structural conformance | existing validators | unify findings under layer output rather than category-only summaries |
| `schema-semantics` | input/output schema usefulness, structured outputs, pagination semantics, naming clarity | `ToolAiReadinessAnalyzer` plus new analyzers | deeper schema and output analysis |
| `safety-boundaries` | prompt injection exposure, open-world risk, error leakage, auth leakage, confirmation needs | security + trust scoring | split safe error hygiene from helpful error clarity |
| `workflow-scenarios` | multi-step CRUD and eventual-consistency validation, recovery and invariants | new subsystem | first-class scenario execution engine |
| `client-profiles` | host-specific compatibility judgments | `Mcp.Benchmark.ClientProfiles` | broaden evidence basis without polluting neutral findings |

### 3. Add an evidence ledger

Every strong finding should point to concrete evidence.

`ValidationFinding` already exists and should remain the canonical rule outcome. What is missing is a stable evidence record to attach it to.

Proposed evidence object:

```csharp
public sealed class ValidationObservation
{
    public required string Id { get; init; }
    public required string LayerId { get; init; }
    public required string Component { get; init; }
    public required string ObservationKind { get; init; }
    public string? ScenarioId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public string? RedactedPayloadPreview { get; init; }
}
```

Examples:

- raw initialize request and response metadata
- tool schema snapshots
- auth challenge metadata
- list pagination traces
- scenario step results
- redacted error payload excerpts

This allows reports, policy, and future dashboards to explain findings without reparsing ad hoc strings.

### 4. Add explicit coverage declarations

The validator should tell users not only what failed, but what was and was not actually tested.

Proposed coverage object:

```csharp
public sealed class ValidationCoverageDeclaration
{
    public required string LayerId { get; init; }
    public required string Scope { get; init; }
    public required ValidationCoverageStatus Status { get; init; }
    public string? Reason { get; init; }
}
```

Examples:

- `workflow-scenarios/create-read-delete` -> `Skipped` because no safe mutating tool was discovered
- `transport-auth/remote-oauth` -> `NotApplicable` for stdio
- `primitive-contracts/resources` -> `Unavailable` because server advertises no resources

This is the missing foundation for honest reporting.

### 5. Add typed descriptors and canonical keys

The plan must not let future implementation drift into ad hoc string matching.

Every layer, observation kind, coverage scope, scenario, pack, and client profile should have:

- a typed runtime descriptor
- a canonical serialized key
- a revision
- a stability label

Important guardrail:

- strings such as `workflow-scenarios/create-read-delete` are acceptable as persisted keys
- business logic should not depend on raw string comparisons scattered across validators, profiles, and reports
- comparisons should flow through typed descriptors and registries

### 6. Add pack registries and applicability resolvers

The current plan adds new structures, but it also needs an explicit extension model so future protocol and host evolution does not collapse back into central switchboards.

Introduce a pack model with explicit registries for:

| Pack type | Owns | Example built-in implementation |
| --- | --- | --- |
| `protocol-features` | negotiated feature sets for protocol revisions | embedded MCP protocol feature pack |
| `rule-pack` | rule descriptors and executable rules for a layer or surface | protocol-core baseline pack |
| `scenario-pack` | workflow scenarios and invariants | safe CRUD scenario pack |
| `client-profile-pack` | host-specific descriptors and evaluation logic | GitHub cloud-agent profile pack |

Resolution inputs should be first-class:

- negotiated protocol version
- schema version chosen from the registry
- transport
- access mode and auth state
- advertised capabilities and surfaces
- selected client profiles
- execution mode hints such as sandboxed, remote, or CI-hosted

Important guardrail:

- applicability should be resolved once per run and persisted as evidence
- validators should consume resolved packs, not re-derive applicability independently

### 7. Prefer feature resolution over hardcoded version branches

Protocol evolution should be modeled through resolved feature sets, not scattered `if version == ...` branches.

The code may still need version comparisons, but they should stay behind a dedicated protocol feature resolver.

That resolver should answer questions such as:

- whether a lifecycle behavior is expected
- whether a notification family is valid for the negotiated version
- whether a transport header or auth behavior applies
- whether a surface such as tasks is normative, optional, experimental, or unavailable

This keeps rule logic focused on behavior, not on version branching trivia.

### 8. Keep authority classification explicit

The repository already classifies findings as `spec`, `guideline`, or `heuristic`. Keep that model and strengthen it.

Do not collapse these:

- MCP spec requirement
- MCP ecosystem guidance
- host documentation requirement
- heuristic or research-backed quality signal

Recommended authority policy:

- Neutral validator findings continue using `ValidationRuleSource`
- Client profile rules continue using `ClientProfileEvidenceBasis`
- Extend `ClientProfileEvidenceBasis` later to cover `Operational` or `Experimental` host constraints when needed

Most important rule:

- host rules stay in `ClientProfiles`
- neutral validators do not emit host-specific failures for product behavior that is not part of the MCP protocol itself

## New Validation Work

### Error Hygiene Analyzer

Current problem:

- the repo can reward verbose errors as good for LLM self-correction
- the repo does not separately penalize stack traces, internal namespaces, raw SQL or ORM leakage, auth metadata leakage, or backend fault signatures

Design:

- keep `AI.TOOL.ERROR.LLM_FRIENDLINESS` or its replacement for recoverability
- add a second analyzer for safety hygiene
- both analyzers produce separate findings and separate subscores

Candidate rule families:

- `AI.ERROR.HYGIENE.STACK_TRACE_LEAK`
- `AI.ERROR.HYGIENE.INTERNAL_IDENTIFIER_LEAK`
- `AI.ERROR.HYGIENE.BACKEND_FAULT_DISCLOSURE`
- `AI.ERROR.HYGIENE.SECRET_OR_TOKEN_ECHO`
- `AI.ERROR.HYGIENE.AUTH_CHALLENGE_DISCLOSURE_MISUSE`

This directly covers the Dynamics/Claude complaint about verbose backend errors.

### Output Contract Analyzer

Current problem:

- response validation is focused on MCP envelope correctness
- there is little judgment on output usability for agents

Design:

- add a dedicated analyzer for `structuredContent`, `outputSchema`, list shape, pagination cues, and field stability
- prefer evidence-backed recommendations over hard failures unless the spec requires the behavior

Candidate checks:

- missing `outputSchema` on tools that clearly return structured records or tabular lists
- list outputs with no stable item keys
- unbounded list tools with no `limit`, `cursor`, `page`, or equivalent semantics
- inconsistent field names across similar tools
- plain text blobs where structured content is obviously available

This directly covers the `list_tables` usability concern.

### Scenario Validator

Current problem:

- one-shot execution cannot validate eventual consistency, read-after-write behavior, or multi-step tool contracts

Design:

- add a scenario subsystem in Infrastructure rather than stuffing more state into `ToolValidator`
- scenario execution consumes discovered tool catalog plus safe capability classifications
- each scenario produces step observations, invariants, and findings

Examples:

- create -> read -> delete
- create -> list -> read
- write -> immediate read with bounded retries
- failed call -> corrected retry
- auth challenge -> authenticate -> successful retrial

Candidate scenario rule families:

- `AI.TOOL.WORKFLOW.CREATE_READ_INCONSISTENT`
- `AI.TOOL.WORKFLOW.EVENTUAL_CONSISTENCY_RETRY_REQUIRED`
- `AI.TOOL.WORKFLOW.DELETE_STILL_VISIBLE`
- `AI.TOOL.WORKFLOW.RETRY_RECOVERABLE`

Important guardrail:

- this subsystem must remain opt-in and safety-aware for mutating tools
- no destructive or expensive scenario should run without explicit configuration and clear annotations

### Deeper Schema Analysis

Current problem:

- current AI-readiness checks are useful but shallow

Design:

- extend schema analysis recursively
- score nested object constraints, string patterns, numeric ranges, required sets, examples, and header-exposed parameters
- validate `structuredContent` against `outputSchema` when present

This should remain `heuristic` or `guideline` unless the spec explicitly requires the behavior.

## Client Profile Evolution

Client profiles should stay as interpretation over neutral evidence, not a competing rule engine.

The current design is already close to correct:

- profiles map documented host expectations to structured rule IDs
- profiles summarize coverage and affected components

The next change should be careful, not large:

1. Add profile rules for surface availability and unsupported combinations where official host docs are explicit.
2. Keep those rules separate from neutral findings.
3. Distinguish hard incompatibility from advisory degradation.

Examples:

- GitHub Cloud Agent: prompts/resources should not raise neutral failures, but the profile should mark them as non-contributing because the host is tools-only.
- GitHub Cloud Agent remote OAuth limitation belongs in profile interpretation, not in protocol conformance.
- Claude Code transport preferences and output-size behaviors should mostly remain advisory unless the host docs state a hard requirement.

## Reporting Architecture

### Shared Report Tree

The report system should stop encoding section logic separately in HTML and markdown.

Add a shared report model builder:

```csharp
ValidationReportDocument Build(ValidationResult result)
```

That document should contain:

- summary cards
- layer summaries
- coverage and blind spots
- scenario summaries
- client profile summaries
- detailed finding rollups
- evidence appendix references

Then:

- HTML renders collapsible sections from the shared tree
- Markdown renders headings and tables from the same tree
- JSON exposes the same hierarchy directly
- SARIF and XML/JUnit flatten only what those formats need, while preserving layer and authority metadata in properties

### Authentication Scenarios

The current auth scenario model is already good enough to support better presentation.

Next step:

- keep the current aligned/review/insecure-style semantics
- render each auth surface as a collapsible section
- show counts in the section summary
- show coverage and skipped reasons up front

This is primarily a reporting model problem, not a validator problem.

### Coverage First

Every format should report three things before detail:

- what was validated
- what was skipped or blocked
- which authority class each finding belongs to

That is the simplest way to stop compact reports from hiding real findings.

## Implementation Strategy

### Phase 1: Add descriptors, pack registries, and applicability resolution

- add typed descriptors for layers, scenarios, observations, coverage scopes, and packs
- add pack registries and applicability resolution in Core and Infrastructure
- replace exact-match version filtering with negotiated feature resolution
- keep the first version code-first and DI-driven rather than introducing external manifests immediately

### Phase 2: Add assessment and evidence subdocuments

- extend `ValidationResult` with dedicated subdocuments for run, assessments, evidence, and compatibility
- remove the old category-first storage shape in the same implementation stretch once equivalent consumers are switched
- adapt policy and trust scoring to consume the new structures when present

### Phase 3: Add error hygiene and output contract analyzers

- separate helpful-error scoring from safe-error scoring
- introduce deeper output and structured-content validation
- keep rule sources honest: mostly `heuristic` and `guideline`

### Phase 4: Add scenario execution

- implement a dedicated scenario engine in Infrastructure
- seed it with safe, bounded scenarios
- expose scenario coverage clearly in reports

### Phase 5: Add shared report section model

- build a renderer-neutral report tree
- migrate HTML first to collapsible sections
- then bring markdown and machine-readable outputs into parity

### Phase 6: Expand client profiles carefully

- add explicit host-surface constraints where the docs are clear
- do not move host policy into neutral validators
- expand evidence basis metadata only as needed

## Testing Strategy

The design is only scalable if the tests mirror the layering.

Add or strengthen:

- architecture tests for project boundaries
- unit tests for finding authority classification and coverage math
- synthetic fake-server fixtures for scenario validation
- report snapshot tests for layer summaries and collapsible HTML structure
- profile tests proving that non-matching category failures do not become false compatibility blockers

## Non-Goals

This plan deliberately does not do the following yet:

- split the solution into more projects immediately
- replace all current result models in one refactor
- treat registry or host acceptance behavior as if it were MCP spec law
- optimize around a single trust score

## Recommended Near-Term Backlog

1. Add typed descriptors and pack metadata in Core.
2. Add pack registries and applicability resolution, reusing the existing schema registry and protocol version seams.
3. Add assessment and evidence subdocuments in `ValidationResult` without deleting current category fields.
4. Introduce an `ErrorHygieneAnalyzer` and attach it to tool and security validation.
5. Introduce an `OutputContractAnalyzer` for list shape, pagination cues, and `outputSchema` alignment.
6. Add a `ScenarioValidator` subsystem for create-read-delete and retry/recovery checks.
7. Build a shared report document model and migrate HTML auth sections to collapsible rendering.
8. Extend client profile rules only where official host docs are explicit, especially for tools-only versus full-surface hosts.

## Bottom Line

The right next move is not a broad rewrite.

The right move is to evolve the current structured-finding architecture into a layer-aware evidence engine:

- protocol correctness stays neutral
- host compatibility stays additive
- workflow validation becomes first-class
- reports become coverage-aware and progressively disclosed
- protocol, host, and rule evolution become pack-driven instead of central-edit driven over time

That path directly addresses the gaps seen in the Dynamics report comparison while preserving the repository's current strengths and keeping the design flexible for future hosts and protocol growth.
