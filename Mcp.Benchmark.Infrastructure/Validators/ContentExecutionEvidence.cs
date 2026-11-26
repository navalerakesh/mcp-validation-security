using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Utilities;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class ContentExecutionEvidence
{
    public static TestStatus Assess(JsonRpcResponse response, string? protocolVersion, string collection)
    {
        if (AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication) return TestStatus.AuthRequired;
        if (response.StatusCode <= 0 || ValidationReliability.ShouldRetryRpcResponse(response)) return TestStatus.Inconclusive;
        if (!response.IsSuccess) return response.StatusCode == 400 ? TestStatus.Inconclusive : TestStatus.Failed;
        if (string.IsNullOrWhiteSpace(response.RawJson)) return TestStatus.Inconclusive;
        if (response.RawJson.Length > ExecutionPolicyDefaults.DefaultMaxResponseBytes ||
            Encoding.UTF8.GetByteCount(response.RawJson) > ExecutionPolicyDefaults.DefaultMaxResponseBytes)
            return TestStatus.Inconclusive;
        try
        {
            using var document = JsonDocument.Parse(response.RawJson, new JsonDocumentOptions
            { AllowDuplicateProperties = false, MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) ||
                !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                return TestStatus.Failed;
            if (ProtocolEraVersions.IsModern(protocolVersion))
            {
                var semantics = ModernResultSemantics.Assess(response.RawJson, protocolVersion);
                if (!semantics.IsValid) return TestStatus.Failed;
                if (semantics.ResultType == McpResultType.InputRequired) return TestStatus.Inconclusive;
            }
            return result.TryGetProperty(collection, out var contents) && contents.ValueKind == JsonValueKind.Array &&
                contents.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object)
                ? TestStatus.Passed : TestStatus.Failed;
        }
        catch (JsonException)
        {
            return TestStatus.Failed;
        }
    }

    public static TestStatus Aggregate(IEnumerable<TestStatus> statuses)
    {
        var observed = statuses.ToHashSet();
        foreach (var status in new[] { TestStatus.Error, TestStatus.Cancelled, TestStatus.Failed, TestStatus.AuthRequired, TestStatus.Inconclusive })
            if (observed.Contains(status)) return status;
        return observed.Any(status => status is not TestStatus.Passed) ? TestStatus.Inconclusive : TestStatus.Passed;
    }
}
