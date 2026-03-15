using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;

namespace Mcp.Benchmark.Infrastructure.Registries;

public interface IProtocolRuleRegistry
{
    IEnumerable<IValidationRule<ProtocolValidationContext>> GetRulesForVersion(string version);
    IEnumerable<IValidationRule<ProtocolValidationContext>> GetAllRules();
    string LatestVersion { get; }
}

public class ProtocolRuleRegistry : IProtocolRuleRegistry
{
    private readonly List<IValidationRule<ProtocolValidationContext>> _rules;

    // This could be moved to a constant or configuration
    public string LatestVersion => "2024-11-25";

    public ProtocolRuleRegistry()
    {
        // Centralized registration of all protocol rules
        _rules = new List<IValidationRule<ProtocolValidationContext>>
        {
            new ContentTypeRule(),
            new CaseSensitivityRule()
            // Future rules for newer versions would be added here
        };
    }

    public IEnumerable<IValidationRule<ProtocolValidationContext>> GetRulesForVersion(string version)
    {
        // In a real scenario, you might want logic to include "all rules up to version X"
        // For now, we filter by exact match as requested
        return _rules.Where(r => r.SpecVersion == version);
    }

    public IEnumerable<IValidationRule<ProtocolValidationContext>> GetAllRules()
    {
        return _rules;
    }
}
