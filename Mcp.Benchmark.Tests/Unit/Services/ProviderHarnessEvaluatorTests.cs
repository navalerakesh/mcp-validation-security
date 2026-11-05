using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ProviderHarnessEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly string TargetDigest = new('a', 64);

    [Fact]
    public void Evaluate_InvalidArguments_ShouldThrow()
    {
        var nullManifest = () => ProviderHarnessEvaluator.Evaluate(null!, TargetDigest, ExecutionMode.Standard, Now);
        var emptyTarget = () => ProviderHarnessEvaluator.Evaluate(CreateManifest([]), "", ExecutionMode.Standard, Now);

        nullManifest.Should().Throw<ArgumentNullException>();
        emptyTarget.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Evaluate_VerifiedAuthorizationAndStableRuns_ShouldVerifyHarness()
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Verified);
        assessment.AuthorizationVerified.Should().BeTrue();
        assessment.AcceptedRunCount.Should().Be(3);
        assessment.RepeatedRunConsistency.Should().Be(1);
        assessment.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_UnverifiedAuthorization_ShouldRemainIncomplete()
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")], ProviderAuthorizationVerificationStatus.Unverified);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Incomplete);
        assessment.Reasons.Should().Contain(reason => reason.Contains("not verified", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_TargetMismatch_ShouldInvalidateHarness()
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, new string('b', 64), ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Invalid);
        assessment.Reasons.Should().Contain(reason => reason.Contains("target identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_InvalidExpectedDigest_ShouldInvalidateEvenWhenManifestDigestIsValid()
    {
        var assessment = ProviderHarnessEvaluator.Evaluate(
            CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")]),
            new string('z', 64),
            ExecutionMode.Standard,
            Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Evaluate_InvalidManifestDigest_ShouldInvalidate(string manifestDigest)
    {
        var manifest = CreateManifest(
            [CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")],
            manifestTargetDigest: manifestDigest);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaZ")]
    public void Evaluate_InvalidGrantDigest_ShouldInvalidate(string grantDigest)
    {
        var manifest = CreateManifest(
            [CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")],
            grantDigest: grantDigest);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Fact]
    public void Evaluate_DivergentSemanticOutcome_ShouldRemainIncomplete()
    {
        var divergent = CreateRun("run-3", SecurityAssuranceStatus.IssuesFound);
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), divergent]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Incomplete);
        assessment.RepeatedRunConsistency.Should().BeApproximately(2d / 3d, 0.0001);
        assessment.Reasons.Should().Contain(reason => reason.Contains("consistency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ExpiredGrantAndMalformedRun_ShouldFailClosed()
    {
        var manifest = CreateManifest([
            CreateRun("duplicate"),
            CreateRun("duplicate"),
            CreateRun("run-3")
        ], expiresAt: Now.AddMinutes(-1));

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Invalid);
        assessment.Reasons.Should().Contain(reason => reason.Contains("not currently valid", StringComparison.Ordinal));
        assessment.Reasons.Should().Contain(reason => reason.Contains("duplicated", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProviderAuthorizationVerificationStatus.Invalid, ProviderHarnessStatus.Invalid)]
    [InlineData(ProviderAuthorizationVerificationStatus.Unverified, ProviderHarnessStatus.Incomplete)]
    public void Evaluate_NonVerifiedGrant_ShouldUseTypedStatus(
        ProviderAuthorizationVerificationStatus verificationStatus,
        ProviderHarnessStatus expected)
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")], verificationStatus);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now).Status.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_DisallowedExecutionMode_ShouldRemainIncomplete()
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Elevated, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Incomplete);
        assessment.Reasons.Should().Contain(reason => reason.Contains("does not allow", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_EmptyProbeAllowlist_ShouldRemainIncomplete()
    {
        var manifest = CreateManifest(
            [CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")],
            probeClasses: []);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Incomplete);
        assessment.Reasons.Should().Contain(reason => reason.Contains("probe-class", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(64, ProviderHarnessStatus.Verified)]
    [InlineData(65, ProviderHarnessStatus.Invalid)]
    public void Evaluate_ProbeAllowlistBoundary_ShouldBeEnforced(int count, ProviderHarnessStatus expected)
    {
        var probes = Enumerable.Range(0, count).Select(index => $"probe-{index}").ToArray();
        var manifest = CreateManifest(
            [CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")],
            probeClasses: probes);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(21)]
    public void Evaluate_InvalidRequiredRunCount_ShouldInvalidate(int requiredRunCount)
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")], requiredRunCount: requiredRunCount);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Theory]
    [InlineData(0.98)]
    [InlineData(1.01)]
    public void Evaluate_InvalidConsistencyThreshold_ShouldInvalidate(double minimumConsistency)
    {
        var manifest = CreateManifest([CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")], minimumConsistency: minimumConsistency);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Fact]
    public void Evaluate_MaximumRunCountAndExactConsistency_ShouldVerify()
    {
        var runs = Enumerable.Range(0, 20).Select(index => CreateRun($"run-{index}")).ToArray();
        var manifest = CreateManifest(runs, requiredRunCount: 20, minimumConsistency: 1);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Verified);
        assessment.AcceptedRunCount.Should().Be(20);
        assessment.RepeatedRunConsistency.Should().Be(1);
    }

    [Fact]
    public void Evaluate_MoreThanMaximumRunReceipts_ShouldInvalidate()
    {
        var runs = Enumerable.Range(0, 21).Select(index => CreateRun($"run-{index}")).ToArray();

        ProviderHarnessEvaluator.Evaluate(CreateManifest(runs), TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Fact]
    public void Evaluate_NoRuns_ShouldNotReportConsistencyFailure()
    {
        var assessment = ProviderHarnessEvaluator.Evaluate(CreateManifest([]), TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Incomplete);
        assessment.RepeatedRunConsistency.Should().Be(0);
        assessment.Reasons.Should().NotContain(reason => reason.Contains("consistency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_MixedExecutionPlans_ShouldInvalidate()
    {
        var runs = new[] { CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3", planDigest: new string('f', 64)) };

        var assessment = ProviderHarnessEvaluator.Evaluate(CreateManifest(runs), TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Invalid);
        assessment.Reasons.Should().Contain(reason => reason.Contains("execution-plan", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RunOutsideAuthorizationWindow_ShouldInvalidate()
    {
        var run = CreateRun("run-1", startedAt: Now.AddHours(-2), endedAt: Now.AddHours(-2).AddMinutes(1));
        var manifest = CreateManifest([run, CreateRun("run-2"), CreateRun("run-3")]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.Status.Should().Be(ProviderHarnessStatus.Invalid);
        assessment.AcceptedRunCount.Should().Be(2);
    }

    [Fact]
    public void Evaluate_AuthorizationAndRunExactBoundaries_ShouldVerify()
    {
        var validFrom = Now.AddHours(-1);
        var runs = new[]
        {
            CreateRun("run-1", startedAt: validFrom, endedAt: validFrom),
            CreateRun("run-2", startedAt: Now.AddMinutes(-1), endedAt: Now),
            CreateRun("run-3", startedAt: Now.AddMinutes(-2), endedAt: Now.AddMinutes(-1))
        };
        var manifest = CreateManifest(runs, validFrom: validFrom, expiresAt: Now.AddHours(1));

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Verified);
    }

    [Fact]
    public void Evaluate_AuthorizationStartingNow_ShouldVerify()
    {
        var runs = new[]
        {
            CreateRun("run-1", startedAt: Now, endedAt: Now),
            CreateRun("run-2", startedAt: Now, endedAt: Now),
            CreateRun("run-3", startedAt: Now, endedAt: Now)
        };
        var manifest = CreateManifest(runs, validFrom: Now, expiresAt: Now.AddHours(1));

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Verified);
    }

    [Fact]
    public void Evaluate_AuthorizationExpiringNow_ShouldRemainIncomplete()
    {
        var manifest = CreateManifest(
            [CreateRun("run-1"), CreateRun("run-2"), CreateRun("run-3")],
            expiresAt: Now);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_ZeroLengthAuthorizationWindow_ShouldRemainIncomplete()
    {
        var boundary = Now.AddMinutes(-5);
        var manifest = CreateManifest(
            [
                CreateRun("run-1", startedAt: boundary, endedAt: boundary),
                CreateRun("run-2", startedAt: boundary, endedAt: boundary),
                CreateRun("run-3", startedAt: boundary, endedAt: boundary)
            ],
            validFrom: boundary,
            expiresAt: boundary);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_FutureRun_ShouldInvalidate()
    {
        var run = CreateRun("run-1", startedAt: Now.AddMinutes(1), endedAt: Now.AddMinutes(2));
        var manifest = CreateManifest([run, CreateRun("run-2"), CreateRun("run-3")]);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Fact]
    public void Evaluate_DuplicateClaimIds_ShouldInvalidate()
    {
        var run = CreateRun("run-1", claimIds: ["claim", "claim"]);
        var manifest = CreateManifest([run, CreateRun("run-2"), CreateRun("run-3")]);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(ProviderHarnessStatus.Invalid);
    }

    [Theory]
    [InlineData(4096, ProviderHarnessStatus.Verified)]
    [InlineData(4097, ProviderHarnessStatus.Invalid)]
    public void Evaluate_ClaimCardinalityBoundary_ShouldBeEnforced(int count, ProviderHarnessStatus expected)
    {
        var claims = Enumerable.Range(0, count).Select(index => $"claim-{index}").ToArray();
        var manifest = CreateManifest([
            CreateRun("run-1", claimIds: claims),
            CreateRun("run-2", claimIds: claims),
            CreateRun("run-3", claimIds: claims)
        ]);

        ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now)
            .Status.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_ClaimOrderingDifference_ShouldRemainConsistent()
    {
        var manifest = CreateManifest([
            CreateRun("run-1", claimIds: ["a", "b"]),
            CreateRun("run-2", claimIds: ["b", "a"]),
            CreateRun("run-3", claimIds: ["a", "b"])
        ]);

        var assessment = ProviderHarnessEvaluator.Evaluate(manifest, TargetDigest, ExecutionMode.Standard, Now);

        assessment.RepeatedRunConsistency.Should().Be(1);
        assessment.Status.Should().Be(ProviderHarnessStatus.Verified);
    }

    private static ProviderHarnessManifest CreateManifest(
        IReadOnlyList<ProviderHarnessRunReceipt> runs,
        ProviderAuthorizationVerificationStatus verificationStatus = ProviderAuthorizationVerificationStatus.Verified,
        DateTimeOffset? expiresAt = null,
        int requiredRunCount = 3,
        double minimumConsistency = 0.99,
        DateTimeOffset? validFrom = null,
        IReadOnlyList<string>? probeClasses = null,
        string? manifestTargetDigest = null,
        string? grantDigest = null)
    {
        return new ProviderHarnessManifest
        {
            ProviderId = "provider-test",
            TargetIdentitySha256 = manifestTargetDigest ?? TargetDigest,
            ProtocolRevision = "2026-07-28",
            Authorization = new ProviderAuthorizationGrant
            {
                GrantId = "grant-1",
                Authority = "provider-security-owner",
                TargetIdentitySha256 = TargetDigest,
                ValidFromUtc = validFrom ?? Now.AddHours(-1),
                ExpiresAtUtc = expiresAt ?? Now.AddHours(1),
                AllowedExecutionModes = [ExecutionMode.Standard],
                AllowedProbeClasses = probeClasses ?? ["protocol-active", "security-non-destructive", "operations-bounded"],
                GrantDocumentSha256 = grantDigest ?? new string('c', 64),
                VerificationStatus = verificationStatus
            },
            RequiredRunCount = requiredRunCount,
            MinimumConsistency = minimumConsistency,
            Runs = runs
        };
    }

    private static ProviderHarnessRunReceipt CreateRun(
        string runId,
        SecurityAssuranceStatus securityStatus = SecurityAssuranceStatus.Verified,
        string? planDigest = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        IReadOnlyList<string>? claimIds = null)
    {
        return new ProviderHarnessRunReceipt
        {
            RunId = runId,
            ResultSha256 = new string(runId[^1] is >= '0' and <= '9' ? runId[^1] : 'd', 64),
            ExecutionPlanSha256 = planDigest ?? new string('e', 64),
            StartedAtUtc = startedAt ?? Now.AddMinutes(-10),
            EndedAtUtc = endedAt ?? Now.AddMinutes(-9),
            CoreConformance = CoreConformanceStatus.Conformant,
            SecurityAssurance = securityStatus,
            OperationalReadiness = OperationalReadinessStatus.Ready,
            NonSuccessClaimIds = claimIds ?? []
        };
    }
}