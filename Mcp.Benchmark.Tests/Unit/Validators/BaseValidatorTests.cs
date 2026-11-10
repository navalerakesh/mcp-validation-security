using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Unit.Validators;

public sealed class BaseValidatorTests
{
    [Theory]
    [InlineData("exception", TestStatus.Error)]
    [InlineData("cancellation", TestStatus.Cancelled)]
    [InlineData("timeout", TestStatus.Inconclusive)]
    public async Task ExecuteValidationAsync_ShouldRetainCollectedEvidenceOnFailure(string failure, TestStatus expectedStatus)
    {
        using var cancellation = new CancellationTokenSource();
        var validator = new AccumulatorValidator();
        ToolTestResult? observed = null;

        var result = await validator.RunAsync((accumulator, token) =>
        {
            observed = accumulator;
            accumulator.ToolResults.Add(new IndividualToolResult { ToolName = "collected" });
            if (failure == "exception") throw new InvalidOperationException("late analyzer failure");
            if (failure == "cancellation") cancellation.Cancel();
            throw new OperationCanceledException(token);
        }, cancellation.Token);

        result.Should().BeSameAs(observed);
        result.ToolResults.Should().ContainSingle().Which.ToolName.Should().Be("collected");
        result.Status.Should().Be(expectedStatus);
    }

    private sealed class AccumulatorValidator() : BaseValidator<AccumulatorValidator>(NullLogger<AccumulatorValidator>.Instance)
    {
        public Task<ToolTestResult> RunAsync(Func<ToolTestResult, CancellationToken, Task<ToolTestResult>> collect, CancellationToken token)
            => ExecuteValidationAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
                "Collection", collect, token);
    }
}