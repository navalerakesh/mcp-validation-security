using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public enum AttackSimulationOutcome
{
    Detected,
    Blocked,
    Inconclusive,
    Skipped
}

public static class AttackSimulationOutcomeResolver
{
    public static AttackSimulationOutcome Resolve(AttackSimulationResult attack)
    {
        return attack.Outcome switch
        {
            ValidationOutcome.Failed => AttackSimulationOutcome.Detected,
            ValidationOutcome.Succeeded => AttackSimulationOutcome.Blocked,
            ValidationOutcome.Inconclusive or ValidationOutcome.Unavailable or ValidationOutcome.Blocked or ValidationOutcome.NotEvaluated => AttackSimulationOutcome.Inconclusive,
            ValidationOutcome.Skipped or ValidationOutcome.NotApplicable => AttackSimulationOutcome.Skipped,
            _ => throw new InvalidOperationException($"Attack simulation used unsupported canonical outcome {attack.Outcome}.")
        };
    }

    public static string ToEvidenceValue(AttackSimulationOutcome outcome) => outcome.ToString();
}