using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;

namespace Mcp.Benchmark.Infrastructure.Registries;

public interface IProtocolRuleRegistry
{
    IReadOnlyList<IVersionedValidationRule<ProtocolValidationContext>> Resolve(ValidationApplicabilityContext context);
    IReadOnlyList<IValidationRulePack<ProtocolValidationContext>> GetPacks();
}

public sealed class ProtocolRuleRegistry : IProtocolRuleRegistry
{
    private readonly IValidationPackRegistry<IValidationRulePack<ProtocolValidationContext>> _packRegistry;

    public ProtocolRuleRegistry(IValidationPackRegistry<IValidationRulePack<ProtocolValidationContext>> packRegistry)
    {
        _packRegistry = packRegistry ?? throw new ArgumentNullException(nameof(packRegistry));
    }

    public ProtocolRuleRegistry()
        : this(new ValidationPackRegistry<IValidationRulePack<ProtocolValidationContext>>(new[] { new BuiltInProtocolRulePack() }))
    {
    }

    public IReadOnlyList<IVersionedValidationRule<ProtocolValidationContext>> Resolve(ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _packRegistry.Resolve(context)
            .SelectMany(pack => pack.GetRules())
            .Where(rule => ValidationPackApplicabilityMatcher.Matches(rule.Applicability, context))
            .ToArray();
    }

    public IReadOnlyList<IValidationRulePack<ProtocolValidationContext>> GetPacks()
    {
        return _packRegistry.GetAll();
    }
}
