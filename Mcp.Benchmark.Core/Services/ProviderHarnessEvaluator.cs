using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Services;

public static class ProviderHarnessEvaluator
{
    private const int MaximumRuns = 20;
    private const int MaximumProbeClasses = 64;
    private const int MaximumClaimIdsPerRun = 4096;

    public static ProviderHarnessAssessment Evaluate(
        ProviderHarnessManifest manifest,
        string expectedTargetIdentitySha256,
        ExecutionMode executionMode,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetIdentitySha256);
        var reasons = new List<string>();
        var invalid = false;

        if (!IsSha256(expectedTargetIdentitySha256) ||
            !IsSha256(manifest.TargetIdentitySha256) ||
            !string.Equals(manifest.TargetIdentitySha256, expectedTargetIdentitySha256, StringComparison.Ordinal))
        {
            reasons.Add("Provider harness target identity does not match the admitted execution target.");
            invalid = true;
        }

        var authorization = manifest.Authorization;
        var authorizationVerified = authorization.VerificationStatus == ProviderAuthorizationVerificationStatus.Verified;
        if (authorization.VerificationStatus == ProviderAuthorizationVerificationStatus.Invalid)
        {
            invalid = true;
        }
        if (!authorizationVerified)
        {
            reasons.Add("Provider authorization grant was not verified by a trusted host adapter.");
        }
        if (!string.Equals(authorization.TargetIdentitySha256, expectedTargetIdentitySha256, StringComparison.Ordinal))
        {
            reasons.Add("Provider authorization grant is bound to a different target identity.");
            invalid = true;
        }
        if (!IsSha256(authorization.GrantDocumentSha256))
        {
            reasons.Add("Provider authorization grant digest is invalid.");
            invalid = true;
        }
        if (authorization.ValidFromUtc > now || authorization.ExpiresAtUtc <= now || authorization.ExpiresAtUtc <= authorization.ValidFromUtc)
        {
            reasons.Add("Provider authorization grant is not currently valid.");
        }
        if (!authorization.AllowedExecutionModes.Contains(executionMode))
        {
            reasons.Add($"Provider authorization does not allow execution mode {executionMode}.");
        }
        if (authorization.AllowedProbeClasses.Count == 0 || authorization.AllowedProbeClasses.Count > MaximumProbeClasses)
        {
            reasons.Add("Provider authorization must contain a bounded, non-empty probe-class allowlist.");
            invalid = authorization.AllowedProbeClasses.Count > MaximumProbeClasses;
        }

        if (manifest.RequiredRunCount < 3 || manifest.RequiredRunCount > MaximumRuns || manifest.Runs.Count > MaximumRuns)
        {
            reasons.Add($"Provider repeated-run count must be between 3 and {MaximumRuns}.");
            invalid = true;
        }
        if (manifest.MinimumConsistency is < 0.99 or > 1.0)
        {
            reasons.Add("Provider minimum consistency must be between 0.99 and 1.0.");
            invalid = true;
        }

        var acceptedRuns = new List<ProviderHarnessRunReceipt>();
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in manifest.Runs.Take(MaximumRuns))
        {
            if (string.IsNullOrWhiteSpace(run.RunId) || !runIds.Add(run.RunId) ||
                !IsSha256(run.ResultSha256) || !IsSha256(run.ExecutionPlanSha256) ||
                run.StartedAtUtc < authorization.ValidFromUtc || run.EndedAtUtc > authorization.ExpiresAtUtc ||
                run.EndedAtUtc > now || run.EndedAtUtc < run.StartedAtUtc ||
                run.NonSuccessClaimIds.Count > MaximumClaimIdsPerRun ||
                run.NonSuccessClaimIds.Distinct(StringComparer.Ordinal).Count() != run.NonSuccessClaimIds.Count)
            {
                reasons.Add($"Provider run receipt '{run.RunId}' is malformed or duplicated.");
                invalid = true;
                continue;
            }
            acceptedRuns.Add(run);
        }

        if (acceptedRuns.Select(run => run.ExecutionPlanSha256).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            reasons.Add("Provider repeated runs must use one execution-plan digest.");
            invalid = true;
        }

        if (acceptedRuns.Count < manifest.RequiredRunCount)
        {
            reasons.Add($"Provider harness has {acceptedRuns.Count} accepted run(s); {manifest.RequiredRunCount} are required.");
        }

        var consistency = CalculateConsistency(acceptedRuns);
        if (acceptedRuns.Count > 0 && consistency < manifest.MinimumConsistency)
        {
            reasons.Add($"Provider repeated-run consistency {consistency:P1} is below {manifest.MinimumConsistency:P1}.");
        }

        var complete = authorizationVerified &&
            authorization.ValidFromUtc <= now && authorization.ExpiresAtUtc > now &&
            authorization.AllowedExecutionModes.Contains(executionMode) &&
            authorization.AllowedProbeClasses.Count is > 0 and <= MaximumProbeClasses &&
            acceptedRuns.Count >= manifest.RequiredRunCount &&
            consistency >= manifest.MinimumConsistency;
        return new ProviderHarnessAssessment
        {
            Status = invalid ? ProviderHarnessStatus.Invalid : complete ? ProviderHarnessStatus.Verified : ProviderHarnessStatus.Incomplete,
            AuthorizationVerified = authorizationVerified,
            TargetIdentitySha256 = manifest.TargetIdentitySha256,
            AcceptedRunCount = acceptedRuns.Count,
            RepeatedRunConsistency = consistency,
            Reasons = reasons.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static double CalculateConsistency(IReadOnlyList<ProviderHarnessRunReceipt> runs)
    {
        if (runs.Count == 0) return 0;
        var mostCommon = runs
            .GroupBy(SemanticOutcomeDigest, StringComparer.Ordinal)
            .Max(group => group.Count());
        return Math.Round(mostCommon / (double)runs.Count, 4);
    }

    private static string SemanticOutcomeDigest(ProviderHarnessRunReceipt run) => CanonicalJsonDigest.ComputeObject(new
    {
        run.CoreConformance,
        run.SecurityAssurance,
        run.OperationalReadiness,
        NonSuccessClaimIds = run.NonSuccessClaimIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()
    });

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');
}