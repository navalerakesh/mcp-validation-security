using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Http;

public sealed class ModernResultSemanticsTests
{
    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"complete\"}}", McpResultType.Complete, true)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"input_required\",\"requestState\":\"state-1\"}}", McpResultType.InputRequired, true)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"input_required\",\"inputRequests\":{\"request-1\":{\"method\":\"roots/list\"}}}}", McpResultType.InputRequired, true)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"input_required\",\"inputRequests\":{\"request-1\":{}}}}", McpResultType.InputRequired, false)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"input_required\"}}", McpResultType.InputRequired, false)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", McpResultType.Invalid, false)]
    public void Assess_ModernResult_ShouldReturnTypedSemantics(string json, McpResultType expectedType, bool expectedValid)
    {
        var result = ModernResultSemantics.Assess(json, "2026-07-28");

        result.ResultType.Should().Be(expectedType);
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Assess_LegacyResultWithoutResultType_ShouldRemainBackwardCompatible()
    {
        var result = ModernResultSemantics.Assess(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            "2025-11-25");

        result.ResultType.Should().Be(McpResultType.Unknown);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_InitializeWithoutResultType_ShouldDeferUntilNegotiatedVersionIsKnown()
    {
        var response = new JsonRpcResponse
        {
            IsSuccess = true,
            RawJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-11-25\"}}"
        };

        ModernResultSemantics.Apply(response, "2026-07-28", "initialize");

        response.IsSuccess.Should().BeTrue();
        response.ResultType.Should().Be(McpResultType.Unknown);
        response.ProtocolSemanticsValid.Should().BeNull();
        response.ProtocolSemanticError.Should().BeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"value\"")]
    public void Assess_ModernNonObjectJson_ShouldReturnTypedInvalidResult(string json)
    {
        var result = ModernResultSemantics.Assess(json, "2026-07-28");

        result.ResultType.Should().Be(McpResultType.Invalid);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("root must be an object");
    }
}