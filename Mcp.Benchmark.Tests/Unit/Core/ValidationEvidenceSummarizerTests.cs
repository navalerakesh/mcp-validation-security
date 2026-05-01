using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Core;

public class ValidationEvidenceSummarizerTests
{
    [Fact]
    public void Summarize_ShouldSeparateCoverageAndConfidence()
    {
        var summary = ValidationEvidenceSummarizer.Summarize([
            new ValidationCoverageDeclaration
            {
                LayerId = "tools",
                Scope = "tools/list",
                Status = ValidationCoverageStatus.Covered,
                Confidence = EvidenceConfidenceLevel.High
            },
            new ValidationCoverageDeclaration
            {
                LayerId = "tools",
                Scope = "tools/call",
                Status = ValidationCoverageStatus.AuthRequired,
                Blocker = ValidationEvidenceBlocker.AuthRequired
            },
            new ValidationCoverageDeclaration
            {
                LayerId = "protocol",
                Scope = "batch",
                Status = ValidationCoverageStatus.Inconclusive,
                Blocker = ValidationEvidenceBlocker.TransientFailure
            },
            new ValidationCoverageDeclaration
            {
                LayerId = "prompts",
                Scope = "prompts/list",
                Status = ValidationCoverageStatus.NotApplicable,
                Blocker = ValidationEvidenceBlocker.NotAdvertised,
                Confidence = EvidenceConfidenceLevel.High
            }
        ]);

        summary.TotalDeclarations.Should().Be(4);
        summary.ApplicableDeclarations.Should().Be(3);
        summary.Covered.Should().Be(1);
        summary.AuthRequired.Should().Be(1);
        summary.Inconclusive.Should().Be(1);
        summary.NotApplicable.Should().Be(1);
        summary.EvidenceCoverageRatio.Should().BeApproximately(0.3333, 0.0001);
        summary.EvidenceConfidenceRatio.Should().BeApproximately(0.5167, 0.0001);
        summary.ConfidenceLevel.Should().Be(EvidenceConfidenceLevel.Low);
    }

    [Theory]
    [InlineData(ValidationCoverageStatus.Blocked, true)]
    [InlineData(ValidationCoverageStatus.Unavailable, true)]
    [InlineData(ValidationCoverageStatus.AuthRequired, false)]
    [InlineData(ValidationCoverageStatus.Inconclusive, false)]
    [InlineData(ValidationCoverageStatus.Covered, false)]
    public void IsCoverageBlocking_ShouldOnlyTreatHardBlockersAsBlocking(ValidationCoverageStatus status, bool expected)
    {
        var coverage = new ValidationCoverageDeclaration
        {
            LayerId = "layer",
            Scope = "scope",
            Status = status
        };

        ValidationEvidenceSummarizer.IsCoverageBlocking(coverage).Should().Be(expected);
    }
}