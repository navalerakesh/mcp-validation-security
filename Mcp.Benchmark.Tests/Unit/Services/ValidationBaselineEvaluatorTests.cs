using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationBaselineEvaluatorTests
{
    [Fact]
    public void BaselineAdmission_ShouldRejectMissingInputsExplicitly()
    {
        var result = CreateResult("current", CertificateEligibilityStatus.Eligible);
        FluentActions.Invoking(() => ValidationBaselineEvaluator.Compare(null!, result)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ValidationBaselineEvaluator.Compare(result, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ValidationBaselineEvaluator.HasAssessmentDegradation(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("document-type")]
    [InlineData("document-version")]
    [InlineData("profile")]
    [InlineData("revision")]
    public void BaselineAdmission_ShouldRejectEachIndependentContextMismatch(string mismatch)
    {
        var baseline = CreateResult("baseline", CertificateEligibilityStatus.Eligible);
        var current = CreateResult("current", CertificateEligibilityStatus.Eligible);
        if (mismatch == "document-type") baseline.DocumentType = "unrelated.document";
        if (mismatch == "document-version") baseline.DocumentSchemaVersion = "1.0.0";
        if (mismatch is "profile" or "revision")
            current = CreateResult("current", CertificateEligibilityStatus.Eligible, profile: new AssuranceProfileManifest
            {
                ProfileId = mismatch == "profile" ? "inventory" : baseline.Assurance.Profile.ProfileId,
                Revision = mismatch == "revision" ? "different-revision" : baseline.Assurance.Profile.Revision
            });
        var failure = FluentActions.Invoking(() => ValidationBaselineEvaluator.Compare(baseline, current))
            .Should().Throw<ArgumentException>().Which;
        failure.ParamName.Should().Be("baseline");
        failure.Message.Should().NotBeNullOrWhiteSpace();
        failure.Message.Should().Contain(mismatch.StartsWith("document", StringComparison.Ordinal) ? "compatible" : "same assurance profile");
    }

    public static IEnumerable<object[]> ClaimTransitions()
    {
        var outcomes = new[] { AssuranceClaimOutcome.ReviewRequired, AssuranceClaimOutcome.Incomplete, AssuranceClaimOutcome.Violated, AssuranceClaimOutcome.Error };
        for (var before = 0; before < outcomes.Length; before++)
        for (var after = 0; after < outcomes.Length; after++)
            yield return [outcomes[before], outcomes[after], after > before];
    }

    [Theory]
    [MemberData(nameof(ClaimTransitions))]
    public void Compare_ShouldDistinguishWorseningFromImprovementForAnExistingClaim(AssuranceClaimOutcome before, AssuranceClaimOutcome after, bool regression)
    {
        var baseline = CreateResult("baseline", CertificateEligibilityStatus.Eligible, "same-claim", before);
        var current = CreateResult("current", CertificateEligibilityStatus.Eligible, "same-claim", after);
        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);
        comparison.IsRegression.Should().Be(regression);
        comparison.NewNonSuccessClaims.Should().HaveCount(regression ? 1 : 0);
        comparison.ResolvedNonSuccessClaimIds.Should().BeEmpty();
        if (regression)
            comparison.NewNonSuccessClaims[0].Should().Be(new BaselineClaimChange("same-claim", AssuranceDimension.Security,
                AssuranceAuthority.DeterministicSecurity, after, "component"));
    }

    public static IEnumerable<object[]> AssessmentTransitions() =>
        from area in Enumerable.Range(0, 4)
        from before in Enumerable.Range(0, 3)
        from after in Enumerable.Range(0, 3)
        select new object[] { area, before, after };

    [Theory]
    [MemberData(nameof(AssessmentTransitions))]
    public void AssessmentDegradation_ShouldRespectEachIndependentOrderedLane(int area, int before, int after)
    {
        var core = new[] { CoreConformanceStatus.Nonconformant, CoreConformanceStatus.Incomplete, CoreConformanceStatus.Conformant };
        var security = new[] { SecurityAssuranceStatus.IssuesFound, SecurityAssuranceStatus.Incomplete, SecurityAssuranceStatus.Verified };
        var operations = new[] { OperationalReadinessStatus.NotReady, OperationalReadinessStatus.Incomplete, OperationalReadinessStatus.Ready };
        var certificate = new[] { CertificateEligibilityStatus.Ineligible, CertificateEligibilityStatus.ReviewRequired, CertificateEligibilityStatus.Eligible };
        var comparison = new AssuranceBaselineComparison
        {
            ProfileId = "provider", ProfileRevision = "fixture",
            CoreConformanceBefore = core[area == 0 ? before : 2], CoreConformanceAfter = core[area == 0 ? after : 2],
            SecurityAssuranceBefore = security[area == 1 ? before : 2], SecurityAssuranceAfter = security[area == 1 ? after : 2],
            OperationalReadinessBefore = operations[area == 2 ? before : 2], OperationalReadinessAfter = operations[area == 2 ? after : 2],
            CertificateEligibilityBefore = certificate[area == 3 ? before : 2], CertificateEligibilityAfter = certificate[area == 3 ? after : 2]
        };
        ValidationBaselineEvaluator.HasAssessmentDegradation(comparison).Should().Be(after < before);
    }

    [Fact]
    public void Compare_ShouldRetainSourceVersionSignedDeltasAndStableChangeOrdering()
    {
        var baseline = CreateResult("baseline", CertificateEligibilityStatus.Eligible,
            claimSet: [Claim("z-old", AssuranceClaimOutcome.Violated), Claim("a-old", AssuranceClaimOutcome.Incomplete)],
            evidence: new() { EvidenceCoverageRatio = 0.25, EvidenceConfidenceRatio = 0.8 });
        baseline.Run.SourceDocumentSchemaVersion = "2.1.0";
        var current = CreateResult("current", CertificateEligibilityStatus.Eligible,
            claimSet: [Claim("z-new", AssuranceClaimOutcome.Error), Claim("a-new", AssuranceClaimOutcome.Violated)],
            evidence: new() { EvidenceCoverageRatio = 0.876543, EvidenceConfidenceRatio = 0.3 });
        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);
        comparison.BaselineValidationId.Should().Be("baseline");
        comparison.BaselineDocumentSchemaVersion.Should().Be("2.1.0");
        comparison.ProfileId.Should().Be(current.Assurance.Profile.ProfileId);
        comparison.ProfileRevision.Should().Be(current.Assurance.Profile.Revision);
        comparison.EvidenceCoverageDelta.Should().Be(0.6265);
        comparison.EvidenceConfidenceDelta.Should().Be(-0.5);
        comparison.NewNonSuccessClaims.Select(claim => claim.ClaimId).Should().Equal("a-new", "z-new");
        comparison.ResolvedNonSuccessClaimIds.Should().Equal("a-old", "z-old");
    }

    [Fact]
    public void Compare_NewViolatedClaim_ShouldReportRegression()
    {
        var baseline = CreateResult("baseline", CertificateEligibilityStatus.Eligible);
        var current = CreateResult("current", CertificateEligibilityStatus.Ineligible, "claim:new");

        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);

        comparison.IsRegression.Should().BeTrue();
        comparison.NewNonSuccessClaims.Should().ContainSingle(change => change.ClaimId == "claim:new");
        comparison.CertificateEligibilityBefore.Should().Be(CertificateEligibilityStatus.Eligible);
        comparison.CertificateEligibilityAfter.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Compare_ResolvedClaimAndImprovedAssessment_ShouldNotReportRegression()
    {
        var baseline = CreateResult("baseline", CertificateEligibilityStatus.Ineligible, "claim:old");
        var current = CreateResult("current", CertificateEligibilityStatus.Eligible);

        var comparison = ValidationBaselineEvaluator.Compare(baseline, current);

        comparison.IsRegression.Should().BeFalse();
        comparison.ResolvedNonSuccessClaimIds.Should().Equal("claim:old");
    }

    private static ValidationResult CreateResult(
        string id,
        CertificateEligibilityStatus eligibility,
        string? claimId = null,
        AssuranceClaimOutcome outcome = AssuranceClaimOutcome.Violated,
        IReadOnlyList<AssuranceClaim>? claimSet = null,
        AssuranceProfileManifest? profile = null,
        EvidenceCoverageSummary? evidence = null)
    {
        var claims = new List<AssuranceClaim>();
        if (claimId != null)
        {
            claims.Add(Claim(claimId, outcome));
        }

        return new ValidationResult
        {
            ValidationId = id,
            Assurance = new AssuranceEvaluation
            {
                Profile = profile ?? Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Provider,
                Claims = claimSet ?? claims,
                EvidenceSummary = evidence ?? new(),
                Assessments = new AssuranceAssessmentSet
                {
                    CoreConformance = new CoreConformanceAssessment { Status = eligibility == CertificateEligibilityStatus.Eligible ? CoreConformanceStatus.Conformant : CoreConformanceStatus.Incomplete },
                    SecurityAssurance = new SecurityAssuranceAssessment { Status = eligibility == CertificateEligibilityStatus.Eligible ? SecurityAssuranceStatus.Verified : SecurityAssuranceStatus.Incomplete },
                    OperationalReadiness = new OperationalReadinessAssessment { Status = eligibility == CertificateEligibilityStatus.Eligible ? OperationalReadinessStatus.Ready : OperationalReadinessStatus.Incomplete }
                },
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "provider",
                    Status = eligibility
                }
            }
        };
    }

    private static AssuranceClaim Claim(string id, AssuranceClaimOutcome outcome) => new()
    {
        ClaimId = id, Subject = "component", Dimension = AssuranceDimension.Security,
        RequirementLevel = AssuranceRequirementLevel.SecurityControl, Authority = AssuranceAuthority.DeterministicSecurity,
        Source = new AssuranceSourceReference { Revision = "test" }, Outcome = outcome, Summary = "Test claim."
    };
}
