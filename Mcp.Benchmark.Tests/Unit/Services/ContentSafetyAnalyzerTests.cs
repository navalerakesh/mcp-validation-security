using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ContentSafetyAnalyzerTests
{
    private readonly ContentSafetyAnalyzer _analyzer;

    public ContentSafetyAnalyzerTests()
    {
        _analyzer = new ContentSafetyAnalyzer(new Mock<ILogger<ContentSafetyAnalyzer>>().Object);
    }

    [Fact]
    public void AnalyzeTool_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzeTool("get_weather");
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeTool_WithNullName_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzeTool(null!);
        findings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeResource_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzeResource("doc.txt", "file:///doc.txt");
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeResource_WithNullValues_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzeResource(null!, null!);
        findings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzePrompt_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzePrompt("code_review", "Reviews code quality", 2);
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzePrompt_WithNullValues_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzePrompt(null!, null, 0);
        findings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeTool_WithPublicUnauthenticatedContext_ShouldEscalateMediumRiskOperation()
    {
        var context = new ContentSafetyAnalysisContext
        {
            Profile = ContentSafetyContextProfile.PublicUnauthenticated,
            ServerProfile = McpServerProfile.Public
        };

        var findings = _analyzer.AnalyzeTool("update_customer_record", context);

        var finding = findings.Should().ContainSingle().Subject;
        finding.RiskLevel.Should().Be(ContentRiskLevel.High);
        finding.RiskScore.Should().BeGreaterThanOrEqualTo(95.0);
        finding.Context.Should().ContainKey("contextProfile").WhoseValue.Should().Be("PublicUnauthenticated");
        finding.Context.Should().ContainKey("baseRiskLevel").WhoseValue.Should().Be("Medium");
        finding.Context.Should().ContainKey("severityAdjustment").WhoseValue.Should().Be("public-anonymous-escalation");
    }

    [Fact]
    public void AnalyzeTool_WithEnterpriseControls_ShouldReduceGovernedHighRiskOperation()
    {
        var context = new ContentSafetyAnalysisContext
        {
            Profile = ContentSafetyContextProfile.EnterpriseGoverned,
            ServerProfile = McpServerProfile.Enterprise,
            AuthenticationRequired = true,
            ObservedControls = new[]
            {
                new AiSafetyControlEvidence
                {
                    ControlKind = AiSafetyControlKind.AuditTrail,
                    Status = AiSafetyControlStatus.Declared
                },
                new AiSafetyControlEvidence
                {
                    ControlKind = AiSafetyControlKind.DestructiveActionConfirmation,
                    Status = AiSafetyControlStatus.Declared
                }
            }
        };

        var findings = _analyzer.AnalyzeTool("delete_database", context);

        var finding = findings.Should().ContainSingle().Subject;
        finding.RiskLevel.Should().Be(ContentRiskLevel.Medium);
        finding.RiskScore.Should().BeLessThanOrEqualTo(70.0);
        finding.Context.Should().ContainKey("contextProfile").WhoseValue.Should().Be("EnterpriseGoverned");
        finding.Context.Should().ContainKey("relevantControlsDeclared").WhoseValue.Should().Be(true);
        finding.Context.Should().ContainKey("severityAdjustment").WhoseValue.Should().Be("enterprise-controls-reduced-risk");
    }

    [Fact]
    public void ContentSafetyContext_FromServerConfig_ShouldInferLocalDeveloperForStdio()
    {
        var context = ContentSafetyAnalysisContext.FromServerConfig(new McpServerConfig
        {
            Endpoint = "npx test-server",
            Transport = "stdio"
        });

        context.Profile.Should().Be(ContentSafetyContextProfile.LocalDeveloper);
        context.AuthenticationRequired.Should().BeFalse();
    }
}
