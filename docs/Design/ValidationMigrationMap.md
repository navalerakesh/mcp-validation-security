# MCP Validator Migration Map

## Intent

This document maps the current codebase seams to the first registries, descriptors, and resolvers needed by the extensibility design.

The goal is to evolve the current architecture, not replace it with a parallel system.

## First-Class Targets

| Current type or seam | Future role | First change to make |
| --- | --- | --- |
| `Mcp.Compliance.Spec/SchemaRegistryProtocolVersions.cs` | schema-version resolver feeding protocol feature resolution | keep as the source of truth for embedded schema selection; add a protocol feature resolver on top of it instead of duplicating version logic |
| `Mcp.Compliance.Spec/ProtocolVersions.cs` | protocol revision constants only | keep constants, but stop treating this file as the complete feature model |
| `Mcp.Benchmark.Infrastructure/Registries/ProtocolRuleRegistry.cs` | built-in protocol rule pack plus registry adapter | replace exact-match string filtering and hardcoded `LatestVersion` with pack descriptors and applicability resolution |
| `Mcp.Benchmark.Core/Abstractions/IValidationRule.cs` | leaf rule execution contract | keep it, but add descriptor and applicability-aware wrappers rather than making each rule own version branching |
| `Mcp.Benchmark.Core/Models/ValidationFinding.cs` | stable baseline rule descriptor catalog | keep as the baseline registry for canonical rule ids; move fast-growing host and heuristic families into pack registration rather than one central dictionary |
| `Mcp.Benchmark.Core/Models/ValidationResults.cs` | persisted result envelope | add subdocuments for run, assessments, evidence, and compatibility before adding more top-level fields |
| `Mcp.Benchmark.Infrastructure/Services/ValidationSessionBuilder.cs` | applicability-context builder input | extend session output with the facts needed to resolve packs once per run |
| `Mcp.Benchmark.Infrastructure/Services/McpValidatorService.cs` | orchestration root and pack coordinator | resolve applicable packs once, then execute validators and scenarios against that resolved plan |
| `Mcp.Benchmark.ClientProfiles/ClientProfileCatalog.cs` | built-in client profile pack data source | migrate descriptors and aliases behind a profile pack and revision-aware resolver |
| `Mcp.Benchmark.ClientProfiles/ClientProfileEvaluator.cs` | client compatibility engine over resolved profile packs | split selection from evaluation so host rules stop being a single hardcoded matrix |
| `Mcp.Benchmark.Infrastructure/Services/ToolAiReadinessAnalyzer.cs` | collaborator used by schema-semantics and error-hygiene packs | keep analyzer logic reusable, but stop making one analyzer the only expansion point for future usability checks |
| `Mcp.Benchmark.Infrastructure/Services/Reporting/MarkdownReportGenerator.cs` and HTML report composers | renderers over a shared report document | add a report document builder before adding more format-specific section logic |

## Registry And Resolver Sequence

### 1. Applicability Resolution

Create these first:

- `IValidationApplicabilityResolver`
- `ValidationApplicabilityContext`
- `ValidationPackDescriptor`
- `IValidationPackRegistry<TPack>`

Why first:

- every later addition depends on one resolved view of protocol version, surfaces, transport, auth state, and selected profiles
- without this step, every new analyzer will keep re-implementing applicability in its own way

### 2. Protocol Features

Create these second:

- `IProtocolFeaturePack`
- `IProtocolFeatureResolver`
- built-in embedded protocol feature pack

Current code to adapt first:

- `SchemaRegistryProtocolVersions`
- `ProtocolRuleRegistry`
- protocol compliance validator version checks

### 3. Rule Packs

Create these third:

- `IVersionedValidationRule<TContext>`
- `IValidationRulePack<TContext>`

Current code to adapt first:

- protocol rules in `Infrastructure/Rules/Protocol`
- structured finding catalog in `ValidationFinding.cs`
- future error-hygiene and output-contract rule families

### 4. Client Profile Packs

Create these fourth:

- `IClientProfilePack`
- `IClientProfileResolver`

Current code to adapt first:

- `ClientProfileCatalog`
- `ClientProfileEvaluator`
- CLI profile option normalization

### 5. Scenario Packs

Create these fifth:

- `IValidationScenario`
- `IValidationScenarioPack`

Current code to adapt first:

- tool safety classification logic
- auth challenge flows from `ValidationSessionBuilder`
- eventual-consistency checks currently missing from `ToolValidator`

### 6. Report Document Builder

Create this after the evidence and assessment substrate exists:

- renderer-neutral `ValidationReportDocumentBuilder`

Current code to adapt first:

- `MarkdownReportGenerator`
- HTML report document/composer classes
- machine-readable flatteners such as SARIF/XML/JUnit properties

## Class-by-Class Recommendations

### `ProtocolRuleRegistry`

Current issue:

- hardcoded latest version string
- exact-match version filtering
- one central list that must be edited for every new protocol rule

Recommendation:

- move the owned list into a built-in protocol rule pack
- make the registry resolve applicable packs instead of comparing raw strings directly
- do not leave an adapter shim behind in the final state

### `ClientProfileCatalog` And `ClientProfileEvaluator`

Current issue:

- profile descriptors, aliases, revisions, and requirement matrices are centralized in static code
- adding a new host or a host revision requires touching several hardcoded locations

Recommendation:

- keep the existing evaluator semantics
- move descriptors and rule sets behind a built-in client profile pack
- add a resolver that can select a specific profile revision instead of always using the latest

### `ValidationResult`

Current issue:

- it already carries category results, scoring, logs, bootstrap metadata, trust, policy, and compatibility
- each new concern increases clone, serialization, and reporting churn

Recommendation:

- keep it as the persisted envelope
- introduce bounded subdocuments before adding more top-level collections
- remove the old top-level category storage shape once the new consumers are wired

### `ValidationSessionBuilder`

Current issue:

- it already captures the facts later packs need, but those facts are not formalized as one applicability object

Recommendation:

- do not move bootstrap logic out of this class
- add an explicit handoff into `ValidationApplicabilityContext`
- persist the resolved applicability facts into `ValidationResult.Run`

### `McpValidatorService`

Current issue:

- it coordinates validators well, but today it does not resolve or persist a single execution plan for layers, packs, and scenarios

Recommendation:

- keep it as the orchestration root
- add one step after session bootstrap that resolves feature packs, rule packs, scenarios, and selected profile packs
- feed that resolved plan into validators and report building

## Minimal Implementation Order

1. Add Core descriptor and pack abstractions.
2. Add an applicability resolver and in-memory pack registries in Infrastructure.
3. Migrate protocol version and rule resolution to the new substrate.
4. Migrate client profiles to the new substrate.
5. Add evidence and assessment subdocuments.
6. Add new analyzers, scenarios, and report-tree work on top.

## Non-Goals For The First Cut

- external JSON or YAML plugin manifests
- runtime loading of third-party assemblies
- removing the current structured finding catalog
- keeping duplicate legacy result paths or compatibility shims

The first cut should stay simple, explicit, and code-first.