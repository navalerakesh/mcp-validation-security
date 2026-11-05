using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationBaselineEvaluatorTests
{
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
        string? claimId = null)
    {
        var claims = new List<AssuranceClaim>();
        if (claimId != null)
        {
            claims.Add(new AssuranceClaim
            {
                ClaimId = claimId,
                Subject = "component",
                Dimension = AssuranceDimension.Security,
                RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                Authority = AssuranceAuthority.DeterministicSecurity,
                Source = new AssuranceSourceReference { Revision = "test" },
                Outcome = AssuranceClaimOutcome.Violated,
                Summary = "Test claim."
            });
        }

        return new ValidationResult
        {
            ValidationId = id,
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Provider,
                Claims = claims,
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
}
