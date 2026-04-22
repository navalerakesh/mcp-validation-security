using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Core;

/// <summary>
/// Tests for core model classes — constructors, computed properties, clone methods.
/// </summary>
public class CoreModelTests
{
    // ─── ValidationResult ────────────────────────────────────────────
    [Fact]
    public void ValidationResult_Duration_ShouldCalculateFromStartEnd()
    {
        var result = new ValidationResult { StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        result.Duration.Should().NotBeNull();
        result.Duration!.Value.TotalSeconds.Should().BeApproximately(10, 1);
    }

    [Fact]
    public void ValidationResult_Duration_ShouldBeNullWithoutEndTime()
    {
        var result = new ValidationResult();
        result.Duration.Should().BeNull();
    }

    [Fact]
    public void ValidationResult_CloneWithoutSecrets_ShouldPreserveTrustAssessment()
    {
        var result = new ValidationResult
        {
            TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted },
            ServerConfig = new McpServerConfig { Endpoint = "test", Authentication = new AuthenticationConfig { Token = "secret" } }
        };

        var clone = result.CloneWithoutSecrets();

        clone.TrustAssessment.Should().NotBeNull();
        clone.TrustAssessment!.TrustLevel.Should().Be(McpTrustLevel.L4_Trusted);
        clone.ServerConfig.Authentication!.Token.Should().NotBe("secret");
    }

    [Fact]
    public void ValidationResult_CloneWithoutSecrets_ShouldPreserveClientCompatibility()
    {
        var result = new ValidationResult
        {
            ClientCompatibility = new ClientCompatibilityReport
            {
                RequestedProfiles = new List<string> { "claude-code" },
                Assessments = new List<ClientProfileAssessment>
                {
                    new()
                    {
                        ProfileId = "claude-code",
                        DisplayName = "Claude Code",
                        Status = ClientProfileCompatibilityStatus.CompatibleWithWarnings,
                        Summary = "Required checks passed, with 1 advisory gap."
                    }
                }
            },
            ServerConfig = new McpServerConfig
            {
                Endpoint = "test",
                Authentication = new AuthenticationConfig { Token = "secret" }
            }
        };

        var clone = result.CloneWithoutSecrets();

        clone.ClientCompatibility.Should().NotBeNull();
        clone.ClientCompatibility!.Assessments.Should().ContainSingle();
        clone.ClientCompatibility.Assessments[0].ProfileId.Should().Be("claude-code");
        clone.ServerConfig.Authentication!.Token.Should().NotBe("secret");
    }

    [Fact]
    public void McpValidatorConfiguration_CloneWithoutSecrets_ShouldPreserveClientProfiles()
    {
        var configuration = new McpValidatorConfiguration
        {
            ClientProfiles = new ClientProfileOptions
            {
                Profiles = new List<string> { "claude-code", "github-copilot-cli" }
            },
            Server = new McpServerConfig
            {
                Endpoint = "https://test.com",
                Authentication = new AuthenticationConfig { Token = "secret" }
            }
        };

        var clone = configuration.CloneWithoutSecrets();

        clone.ClientProfiles.Should().NotBeNull();
        clone.ClientProfiles!.Profiles.Should().Equal("claude-code", "github-copilot-cli");
        clone.Server.Authentication!.Token.Should().NotBe("secret");
    }

    // ─── McpServerConfig ──────────────────────────────────────────
    [Fact]
    public void McpServerConfig_CloneWithoutSecrets_ShouldRedactAuthHeaders()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com",
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer secret123" },
                { "X-Custom", "visible" }
            }
        };

        var clone = config.CloneWithoutSecrets();

        clone.Headers["Authorization"].Should().Be("__HEADER-REDACTED__");
        clone.Headers["X-Custom"].Should().Be("visible");
    }

    // ─── McpTrustAssessment ──────────────────────────────────────
    [Theory]
    [InlineData(McpTrustLevel.L5_CertifiedSecure, "Certified")]
    [InlineData(McpTrustLevel.L4_Trusted, "Trusted")]
    [InlineData(McpTrustLevel.L3_Acceptable, "Acceptable")]
    [InlineData(McpTrustLevel.L2_Caution, "Caution")]
    [InlineData(McpTrustLevel.L1_Untrusted, "Untrusted")]
    [InlineData(McpTrustLevel.Unknown, "Unknown")]
    public void McpTrustAssessment_TrustLabel_ShouldMatchLevel(McpTrustLevel level, string expectedContains)
    {
        new McpTrustAssessment { TrustLevel = level }.TrustLabel.Should().Contain(expectedContains);
    }

    // ─── LoadTestResult ──────────────────────────────────────────
    [Fact]
    public void LoadTestResult_ErrorRate_ShouldCalculateCorrectly()
    {
        var result = new LoadTestResult { TotalRequests = 100, FailedRequests = 5 };
        result.ErrorRate.Should().BeApproximately(5.0, 0.1);
    }

    [Fact]
    public void LoadTestResult_ErrorRate_ShouldBeZeroWithNoRequests()
    {
        var result = new LoadTestResult { TotalRequests = 0, FailedRequests = 0 };
        result.ErrorRate.Should().Be(0);
    }

    [Fact]
    public void LoadTestResult_NonRateLimitedFailedRequests_ShouldExcludeRateLimitedRequests()
    {
        var result = new LoadTestResult { FailedRequests = 5, RateLimitedRequests = 2 };

        result.NonRateLimitedFailedRequests.Should().Be(3);
    }

    // ─── ScoringConstants ────────────────────────────────────────
    [Fact]
    public void ScoringConstants_WeightsShouldSumTo1()
    {
        var sum = ScoringConstants.WeightProtocol + ScoringConstants.WeightSecurity +
                  ScoringConstants.WeightTools + ScoringConstants.WeightResources +
                  ScoringConstants.WeightPrompts + ScoringConstants.WeightPerformance;
        sum.Should().Be(1.0);
    }

    [Fact]
    public void ScoringConstants_ThresholdsShouldBeOrdered()
    {
        ScoringConstants.TrustL5Threshold.Should().BeGreaterThan(ScoringConstants.TrustL4Threshold);
        ScoringConstants.TrustL4Threshold.Should().BeGreaterThan(ScoringConstants.TrustL3Threshold);
        ScoringConstants.TrustL3Threshold.Should().BeGreaterThan(ScoringConstants.TrustL2Threshold);
    }

    [Fact]
    public void ScoringConstants_VulnPenalties_ShouldDescendBySeverity()
    {
        ScoringConstants.VulnPenaltyCritical.Should().BeGreaterThan(ScoringConstants.VulnPenaltyHigh);
        ScoringConstants.VulnPenaltyHigh.Should().BeGreaterThan(ScoringConstants.VulnPenaltyMedium);
        ScoringConstants.VulnPenaltyMedium.Should().BeGreaterThan(ScoringConstants.VulnPenaltyLow);
        ScoringConstants.VulnPenaltyLow.Should().BeGreaterThan(ScoringConstants.VulnPenaltyInfo);
    }

    // ─── McpComplianceTiers ──────────────────────────────────────
    [Fact]
    public void McpComplianceTiers_MustConstants_ShouldStartWithMUST()
    {
        McpComplianceTiers.Must.InitializeResponse.Should().StartWith("MUST:");
        McpComplianceTiers.Must.ServerInfoPresent.Should().StartWith("MUST:");
        McpComplianceTiers.Must.ToolHasName.Should().StartWith("MUST:");
        McpComplianceTiers.Must.StandardErrorCodes.Should().StartWith("MUST:");
    }

    [Fact]
    public void McpComplianceTiers_ShouldConstants_ShouldStartWithSHOULD()
    {
        McpComplianceTiers.Should.ToolHasDescription.Should().StartWith("SHOULD:");
        McpComplianceTiers.Should.SanitizeToolOutputs.Should().StartWith("SHOULD:");
    }

    [Fact]
    public void McpComplianceTiers_MayConstants_ShouldStartWithMAY()
    {
        McpComplianceTiers.May.InstructionsField.Should().StartWith("MAY:");
        McpComplianceTiers.May.ToolAnnotations.Should().StartWith("MAY:");
        McpComplianceTiers.May.Logging.Should().StartWith("MAY:");
    }

    // ─── ComplianceTierCheck ─────────────────────────────────────
    [Fact]
    public void ComplianceTierCheck_ShouldHaveDefaultValues()
    {
        var check = new ComplianceTierCheck();
        check.Tier.Should().Be("MUST");
        check.Passed.Should().BeFalse();
        check.Component.Should().BeEmpty();
    }

    // ─── AiBoundaryFinding ───────────────────────────────────────
    [Fact]
    public void AiBoundaryFinding_ShouldHaveDefaultSeverity()
    {
        var finding = new AiBoundaryFinding();
        finding.Severity.Should().Be("Medium");
        finding.Category.Should().BeEmpty();
    }
}
