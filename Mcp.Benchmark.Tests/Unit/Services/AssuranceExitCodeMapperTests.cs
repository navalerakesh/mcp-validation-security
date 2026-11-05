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