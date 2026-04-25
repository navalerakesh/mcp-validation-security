# MCP Validator Extensibility Blueprint

## Intent

This document turns `LayeredValidationPlan.md` into implementation-grade contracts for future protocol revisions, host evolution, and layered rule growth.

The design goal is simple:

- future MCP spec revisions should be additive
- future client profiles should be additive
- future rule families should be additive
- older validation artifacts should remain explainable after the system grows

## Design Rules

1. Keep `Core` host-neutral and transport-neutral.
2. Keep built-in extensibility code-first and DI-driven before introducing external manifests.
3. Use typed descriptors in code and canonical keys in serialized artifacts.
4. Resolve applicability once per run and persist that decision.
5. Treat protocol revision handling as feature resolution, not as scattered version conditionals.
6. Treat client profiles as packs over neutral evidence, not as alternate validators.

## Anti-Traps

The main traps to avoid are:

- adding more raw string ids and comparing them everywhere
- centralizing all growth into one static rule catalog or one profile matrix
- storing every new concern directly on `ValidationResult`
- silently evaluating against the latest host assumptions without revision pinning
- branching rules directly on protocol-version strings inside validators

## Core Contract Types

### Stable Keys And Revisions

These types should live in `Mcp.Benchmark.Core/Models`.

```csharp
namespace Mcp.Benchmark.Core.Models;

public readonly record struct ValidationDescriptorKey(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ValidationRevision(string Value)
{
    public override string ToString() => Value;
}

public enum ValidationStability
{
    Stable,
    Preview,
    Experimental,
    Deprecated
}

public enum ValidationPackKind
{
    ProtocolFeatures,
    RulePack,
    ScenarioPack,
    ClientProfilePack
}
```

### Pack Descriptor And Applicability

```csharp
namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationPackDescriptor
{
    public required ValidationDescriptorKey Key { get; init; }

    public required ValidationPackKind Kind { get; init; }

    public required ValidationRevision Revision { get; init; }

    public required string DisplayName { get; init; }

    public required ValidationStability Stability { get; init; }

    public string? DocumentationUrl { get; init; }
}

public sealed class ValidationApplicability
{
    public IReadOnlyList<string> ProtocolVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SchemaVersions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AccessModes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredSurfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ClientProfiles { get; init; } = Array.Empty<string>();

    public bool? RequiresAuthentication { get; init; }
}

public sealed class ValidationApplicabilityContext
{
    public required string NegotiatedProtocolVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public required string Transport { get; init; }

    public required string AccessMode { get; init; }

    public string? ServerProfile { get; init; }

    public bool IsAuthenticated { get; init; }

    public IReadOnlyList<string> AdvertisedCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdvertisedSurfaces { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SelectedClientProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> EnvironmentHints { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
```

## Core Extension Interfaces

These interfaces should live in `Mcp.Benchmark.Core/Abstractions`.

### Common Pack Contracts

```csharp
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public interface IValidationPack
{
    ValidationPackDescriptor Descriptor { get; }

    ValidationApplicability Applicability { get; }
}

public interface IValidationPackRegistry<TPack> where TPack : IValidationPack
{
    IReadOnlyList<TPack> GetAll();

    IReadOnlyList<TPack> Resolve(ValidationApplicabilityContext context);
}

public interface IValidationApplicabilityResolver
{
    ValidationApplicabilityContext Build(
        ValidationSessionContext session,
        McpValidatorConfiguration configuration,
        IReadOnlyList<string> selectedClientProfiles);
}
```

### Protocol Feature Resolution

```csharp
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public sealed class ProtocolFeatureSet
{
    public required string NegotiatedProtocolVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public bool RequiresHttpProtocolHeader { get; init; }

    public bool SupportsToolListChangedNotifications { get; init; }

    public bool SupportsTasksSurface { get; init; }

    public bool SupportsDeferredWorkflows { get; init; }

    public IReadOnlyList<string> OptionalCapabilities { get; init; } = Array.Empty<string>();
}

public interface IProtocolFeaturePack : IValidationPack
{
    ProtocolFeatureSet BuildFeatureSet(ValidationApplicabilityContext context);
}

public interface IProtocolFeatureResolver
{
    ProtocolFeatureSet Resolve(ValidationApplicabilityContext context);
}
```

### Rule Packs

Keep `IValidationRule<TContext>` as the leaf execution concept, but stop using the `SpecVersion` string as the primary applicability mechanism.

Target contract:

```csharp
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public interface IVersionedValidationRule<TContext> : IValidationRule<TContext>
{
    ValidationRuleDescriptor Descriptor { get; }

    ValidationApplicability Applicability { get; }
}

public interface IValidationRulePack<TContext> : IValidationPack
{
    IReadOnlyList<IVersionedValidationRule<TContext>> GetRules();
}
```

Migration note:

- current `IValidationRule<TContext>.SpecVersion` should stop being an applicability decision point once pack resolution exists
- new registries should resolve pack applicability first and then execute leaf rules

### Scenario Packs

```csharp
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public sealed class ValidationScenarioDescriptor
{
    public required ValidationDescriptorKey Key { get; init; }

    public required ValidationRevision Revision { get; init; }

    public required string DisplayName { get; init; }

    public required string LayerKey { get; init; }

    public required ValidationStability Stability { get; init; }

    public bool MutatesState { get; init; }
}

public interface IValidationScenario
{
    ValidationScenarioDescriptor Descriptor { get; }

    Task<ValidationScenarioResult> ExecuteAsync(
        ValidationScenarioContext context,
        CancellationToken cancellationToken);
}

public interface IValidationScenarioPack : IValidationPack
{
    IReadOnlyList<IValidationScenario> GetScenarios();
}
```

### Client Profile Packs

```csharp
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public sealed class ResolvedClientProfile
{
    public required ClientProfileDescriptor Descriptor { get; init; }

    public required ValidationRevision Revision { get; init; }

    public required ValidationStability Stability { get; init; }
}

public interface IClientProfilePack : IValidationPack
{
    IReadOnlyList<ClientProfileDescriptor> GetProfiles();

    ClientProfileAssessment Evaluate(
        ClientProfileDescriptor profile,
        ValidationResult validationResult,
        ValidationApplicabilityContext applicabilityContext);
}

public interface IClientProfileResolver
{
    IReadOnlyList<ResolvedClientProfile> Resolve(
        ClientProfileOptions? options,
        ValidationApplicabilityContext applicabilityContext);
}
```

## Result Envelope Shape

The target state for `ValidationResult` is a stable envelope with bounded subdocuments.

```csharp
namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationRunDocument
{
    public string ValidationId { get; init; } = string.Empty;

    public string? ProtocolVersion { get; init; }

    public string? SchemaVersion { get; init; }

    public ValidationApplicabilityContext? ApplicabilityContext { get; init; }
}

public sealed class ValidationAssessmentDocument
{
    public List<ValidationLayerResult> Layers { get; init; } = new();

    public List<ValidationScenarioResult> Scenarios { get; init; } = new();
}

public sealed class ValidationEvidenceDocument
{
    public List<ValidationObservation> Observations { get; init; } = new();

    public List<ValidationCoverageDeclaration> Coverage { get; init; } = new();

    public List<ValidationPackDescriptor> AppliedPacks { get; init; } = new();
}

public sealed class ValidationCompatibilityDocument
{
    public ClientCompatibilityReport? ClientCompatibility { get; init; }
}
```

Important rule:

- report documents are derived from `ValidationResult`
- report documents are not the source of truth
- duplicate legacy result shapes should not remain after the cutover

## Registry Placement

The first implementation should keep the split simple.

| Concern | Project | First concrete type |
| --- | --- | --- |
| Pack contracts and descriptors | `Mcp.Benchmark.Core` | new interfaces and models from this document |
| Built-in protocol feature pack | `Mcp.Compliance.Spec` or `Infrastructure` | embedded MCP protocol feature pack |
| Pack registries and applicability resolver | `Mcp.Benchmark.Infrastructure` | DI-backed in-memory registries |
| Built-in client profile pack | `Mcp.Benchmark.ClientProfiles` | built-in profile pack |
| Report document builder | `Mcp.Benchmark.Infrastructure` | renderer-neutral report builder |

## Versioning Policy

1. Persist both negotiated protocol version and schema version.
2. Persist applied pack descriptors and revisions for every run.
3. Allow client profile evaluation to target a specific revision instead of always using the newest available assumptions.
4. Keep built-in packs in code until contributor or deployment needs justify external manifests.

## Implementation Order

1. Add descriptor and pack models in `Core`.
2. Add applicability resolution and in-memory registries in `Infrastructure`.
3. Adapt protocol version handling to use `SchemaRegistryProtocolVersions` plus protocol feature resolution.
4. Migrate the current protocol rule registry into a built-in rule pack.
5. Migrate the current client profile catalog and evaluator into a built-in client profile pack plus resolver.
6. Replace the old category-first result storage shape with the new bounded subdocuments.
7. Add scenario packs and report document builders on top of that foundation.
