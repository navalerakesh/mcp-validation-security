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
        ProviderAuthorizationVerification authorizationVerification,
        string expectedTargetIdentitySha256,
        string expectedProtocolRevision,
        string? expectedExecutionPlanSha256,
        ExecutionMode executionMode,
        DateTimeOffset now,
        IReadOnlyList<string> expectedProbeClasses,
        ProviderHarnessRunReceipt? currentRun = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(authorizationVerification);
        ArgumentNullException.ThrowIfNull(expectedProbeClasses);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetIdentitySha256);
        var reasons = new List<string>();
        var invalid = false;

        if (string.IsNullOrWhiteSpace(manifest.ProviderId))
        {
            reasons.Add("Provider harness provider identity is required.");
            invalid = true;
        }

        if (!IsSha256(expectedTargetIdentitySha256) ||
            !IsSha256(manifest.TargetIdentitySha256) ||
            !string.Equals(manifest.TargetIdentitySha256, expectedTargetIdentitySha256, StringComparison.Ordinal))
        {
            reasons.Add("Provider harness target identity does not match the admitted execution target.");
            invalid = true;
        }
        if (string.IsNullOrWhiteSpace(expectedProtocolRevision) ||
            !string.Equals(manifest.ProtocolRevision, expectedProtocolRevision, StringComparison.Ordinal))
        {
            reasons.Add("Provider harness protocol revision does not match the admitted execution plan.");
            invalid = true;
        }
        if (expectedExecutionPlanSha256 != null && !IsSha256(expectedExecutionPlanSha256))
        {
            reasons.Add("Admitted provider execution-plan digest is invalid.");
            invalid = true;
        }

        var authorization = manifest.Authorization;
        if (string.IsNullOrWhiteSpace(authorization.GrantId) || string.IsNullOrWhiteSpace(authorization.Authority))
        {
            reasons.Add("Provider authorization grant identity and authority are required.");
            invalid = true;
        }
        var authorizationVerified = authorizationVerification.Status == ProviderAuthorizationVerificationStatus.Verified &&
            string.Equals(authorizationVerification.GrantId, authorization.GrantId, StringComparison.Ordinal);
        if (authorizationVerification.Status == ProviderAuthorizationVerificationStatus.Invalid)
        {
            invalid = true;
        }
        if (!authorizationVerified)
        {
            reasons.Add("Provider authorization grant was not verified by a trusted host adapter.");
        }
        reasons.AddRange(authorizationVerification.Reasons);
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
        var unauthorizedProbeClasses = expectedProbeClasses
            .Distinct(StringComparer.Ordinal)
            .Where(probeClass => !authorization.AllowedProbeClasses.Contains(probeClass, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unauthorizedProbeClasses.Length > 0)
        {
            reasons.Add($"Provider authorization does not allow planned probe class(es): {string.Join(", ", unauthorizedProbeClasses)}.");
            invalid = true;
        }

        var totalRunCount = manifest.Runs.Count + (currentRun == null ? 0 : 1);
        if (manifest.RequiredRunCount < 3 || manifest.RequiredRunCount > MaximumRuns || totalRunCount > MaximumRuns)
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
        foreach (var run in manifest.Runs.Append(currentRun).Where(run => run != null).Cast<ProviderHarnessRunReceipt>().Take(MaximumRuns))
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
        if (expectedExecutionPlanSha256 != null &&
            acceptedRuns.Any(run => !string.Equals(run.ExecutionPlanSha256, expectedExecutionPlanSha256, StringComparison.Ordinal)))
        {
            reasons.Add("Provider run receipts do not match the admitted execution-plan digest.");
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
            unauthorizedProbeClasses.Length == 0 &&
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