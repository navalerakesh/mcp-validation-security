using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class AssuranceExitCodeMapperTests
{
    [Fact]
    public void Map_InventoryProfile_ShouldUseSelectedPolicyWithoutTreatingCertificateIneligibilityAsFailure()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Inventory, CertificateEligibilityStatus.Ineligible);

        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 })
            .Should().Be(AssuranceExitCodeMapper.Satisfied);
    }

    [Fact]
    public void Map_CiProfile_ShouldDistinguishIncompleteEvidenceFromFailure()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible);

        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 })
            .Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    [Theory]
    [InlineData(CertificateEligibilityStatus.Eligible, 0)]
    [InlineData(CertificateEligibilityStatus.Ineligible, 1)]
    [InlineData(CertificateEligibilityStatus.ReviewRequired, 2)]
    public void Map_ProviderProfile_ShouldFollowCertificateEligibility(CertificateEligibilityStatus status, int expected)
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Provider, status);

        AssuranceExitCodeMapper.Map(assurance, null).Should().Be(expected);
    }

    [Fact]
    public void Map_WholeTargetUnavailable_ShouldReturnSeventyFive()
    {
        var result = new ValidationResult
        {
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible)
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "target",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline
        });

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.TargetUnavailable);
    }

    [Fact]
    public void Map_FrameworkIntegrityError_ShouldReturnSeventy()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Error,
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible),
            CriticalErrors = ["Internal validator failure."]
        };

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.RuntimeIntegrityFailure);
    }

    [Fact]
    public void Map_CiIncompleteAssessment_ShouldReturnTwo()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.PartiallyCompleted,
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible),
            PolicyOutcome = new ValidationPolicyOutcome { Passed = true }
        };

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    private static AssuranceEvaluation CreateEvaluation(
        AssuranceProfileManifest profile,
        CertificateEligibilityStatus certificateStatus)
    {
        return new AssuranceEvaluation
        {
            Profile = profile,
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = profile.ProfileId,
                Status = certificateStatus
            }
        };
    }
}