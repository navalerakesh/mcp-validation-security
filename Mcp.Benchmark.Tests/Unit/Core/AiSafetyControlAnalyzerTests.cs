using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Core;

public class AiSafetyControlAnalyzerTests
{
    [Fact]
    public void AnalyzeTool_WithDestructiveToolMissingConfirmation_ShouldRecordMissingHumanControlEvidence()
    {
        var analysis = AiSafetyControlAnalyzer.AnalyzeTool(new AiSafetyControlTarget
        {
            Name = "delete_repo",
            Description = "Deletes a repository.",
            DestructiveHint = true,
            OpenWorldHint = true
        });

        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.UserConfirmation
            && evidence.Status == AiSafetyControlStatus.Missing);
        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.DestructiveActionConfirmation
            && evidence.Status == AiSafetyControlStatus.Missing
            && evidence.Authority == ValidationRuleSource.Guideline);
        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.DataSharingDisclosure
            && evidence.Status == AiSafetyControlStatus.Declared);
        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.HostServerResponsibilitySplit
            && evidence.Status == AiSafetyControlStatus.NotObservable);
    }

    [Fact]
    public void AnalyzeTool_WithReadOnlyTool_ShouldMarkConfirmationNotApplicableButStillReportHostBoundary()
    {
        var analysis = AiSafetyControlAnalyzer.AnalyzeTool(new AiSafetyControlTarget
        {
            Name = "list_repos",
            Description = "Lists repositories.",
            ReadOnlyHint = true,
            DestructiveHint = false,
            OpenWorldHint = false
        });

        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.UserConfirmation
            && evidence.Status == AiSafetyControlStatus.NotApplicable);
        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.DataSharingDisclosure
            && evidence.Status == AiSafetyControlStatus.Declared);
        analysis.Evidence.Should().Contain(evidence =>
            evidence.ControlKind == AiSafetyControlKind.HostServerResponsibilitySplit
            && evidence.Status == AiSafetyControlStatus.NotObservable);
    }

    [Fact]
    public void AnalyzeTool_WithoutOpenWorldHint_ShouldRecordExplicitAbsenceAndEffectiveDefault()
    {
        var analysis = AiSafetyControlAnalyzer.AnalyzeTool(new AiSafetyControlTarget
        {
            Name = "search_docs",
            Description = "Searches documentation.",
            ReadOnlyHint = true
        });

        var evidence = analysis.Evidence.Should().ContainSingle(item =>
            item.ControlKind == AiSafetyControlKind.DataSharingDisclosure).Subject;
        evidence.Status.Should().Be(AiSafetyControlStatus.Missing);
        evidence.Summary.Should().Contain("MCP applies the openWorldHint=true default");
        evidence.Metadata["openWorldHint"].Should().Be("notDeclared");
        evidence.Metadata["effectiveOpenWorldHint"].Should().Be("True");
    }
}
