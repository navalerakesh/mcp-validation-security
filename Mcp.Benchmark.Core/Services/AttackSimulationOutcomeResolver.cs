using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public enum AttackSimulationOutcome
{
    Detected,
    Blocked,
    Skipped
}

public static class AttackSimulationOutcomeResolver
{
    public static AttackSimulationOutcome Resolve(AttackSimulationResult attack)
    {
        if (attack.Evidence.TryGetValue("outcome", out var outcomeValue) &&
            outcomeValue is string outcomeText &&
            Enum.TryParse(outcomeText, ignoreCase: true, out AttackSimulationOutcome parsedOutcome))
        {
            return parsedOutcome;
        }

        if (!string.IsNullOrWhiteSpace(attack.ServerResponse) &&
            attack.ServerResponse.Contains("Skipped", StringComparison.OrdinalIgnoreCase))
        {
            return AttackSimulationOutcome.Skipped;
        }

        return attack.AttackSuccessful ? AttackSimulationOutcome.Detected : AttackSimulationOutcome.Blocked;
    }

    public static string ToEvidenceValue(AttackSimulationOutcome outcome) => outcome.ToString();
}