using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;

namespace Mcp.Benchmark.Infrastructure.Registries;

public sealed class BuiltInProtocolRulePack : IValidationRulePack<ProtocolValidationContext>
{
    private readonly IReadOnlyList<IVersionedValidationRule<ProtocolValidationContext>> _rules =
    [
        new ContentTypeRule(),
        new CaseSensitivityRule()
    ];

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("rule-pack/protocol-core"),
        Kind = ValidationPackKind.RulePack,
        Revision = new ValidationRevision("2026-04"),
        DisplayName = "Built-in Protocol Rule Pack",
        Stability = ValidationStability.Stable,
        DocumentationUrl = "https://spec.modelcontextprotocol.io/"
    };

    public ValidationApplicability Applicability => new();

    public IReadOnlyList<IVersionedValidationRule<ProtocolValidationContext>> GetRules()
    {
        return _rules;
    }
}