using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationCoverageFactoryTests
{
    [Theory]
    [InlineData(ValidationOutcome.Succeeded, ValidationCoverageStatus.Covered)]
    [InlineData(ValidationOutcome.Failed, ValidationCoverageStatus.Covered)]
    [InlineData(ValidationOutcome.Error, ValidationCoverageStatus.Covered)]
    [InlineData(ValidationOutcome.Skipped, ValidationCoverageStatus.Skipped)]
    [InlineData(ValidationOutcome.AuthRequired, ValidationCoverageStatus.AuthRequired)]
    [InlineData(ValidationOutcome.Inconclusive, ValidationCoverageStatus.Inconclusive)]
    [InlineData(ValidationOutcome.NotApplicable, ValidationCoverageStatus.NotApplicable)]
    [InlineData(ValidationOutcome.Unavailable, ValidationCoverageStatus.Unavailable)]
    [InlineData(ValidationOutcome.Blocked, ValidationCoverageStatus.Blocked)]
    [InlineData(ValidationOutcome.Cancelled, ValidationCoverageStatus.Blocked)]
    [InlineData(ValidationOutcome.NotEvaluated, ValidationCoverageStatus.Unavailable)]
    [InlineData(ValidationOutcome.Running, ValidationCoverageStatus.Unavailable)]
    public void FromOutcome_ShouldMapEveryCanonicalOutcome(
        ValidationOutcome outcome,
        ValidationCoverageStatus expectedStatus)
    {
        var coverage = ValidationCoverageFactory.FromOutcome("layer", "scope", outcome, "reason");

        coverage.Status.Should().Be(expectedStatus);
        coverage.Outcome.Should().Be(outcome is ValidationOutcome.NotEvaluated or ValidationOutcome.Running
            ? ValidationOutcome.Unavailable
            : outcome);
    }
}