using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;

namespace Mcp.Benchmark.Infrastructure.Attacks;

/// <summary>
/// Simulates an AI agent sending malformed tool calls (hallucinations).
/// Sends wrong argument types to discovered tools and grades the error response
/// for clarity — does the error help the AI self-correct?
/// </summary>
public class HallucinationFuzzer : BaseAttackVector
{
    public HallucinationFuzzer(ILogger<HallucinationFuzzer> logger) : base(logger) { }

    public override string Id => "MCP-AI-001";
    public override string Name => "Hallucination Fuzzing";
    public override string Category => "AI Readiness";
    public override string Description => "Sends type-mismatched arguments to tools and grades error message clarity for AI self-correction.";

    public override async Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken)
    {
        // 1. Discover tools
        var toolsResponse = await client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, cancellationToken);

        if (!toolsResponse.IsSuccess || string.IsNullOrEmpty(toolsResponse.RawJson))
        {
            return CreateSkippedResult("Skipped: Could not list tools for fuzzing.", "No tools available.", "Low", CollectProbeContexts(toolsResponse.ProbeContext));
        }

        // 2. Find a tool with typed parameters
        string? toolName = null;
        string? targetParam = null;
        string? expectedType = null;

        try
        {
            using var doc = JsonDocument.Parse(toolsResponse.RawJson);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("tools", out var tools) &&
                tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    if (tool.TryGetProperty("name", out var name) &&
                        tool.TryGetProperty("inputSchema", out var schema) &&
                        schema.TryGetProperty("properties", out var props))
                    {
                        foreach (var prop in props.EnumerateObject())
                        {
                            if (prop.Value.TryGetProperty("type", out var type))
                            {
                                var typeStr = type.GetString();
                                // Prefer non-string types for type mismatch testing
                                if (typeStr is "integer" or "number" or "boolean" or "array" or "object")
                                {
                                    toolName = name.GetString();
                                    targetParam = prop.Name;
                                    expectedType = typeStr;
                                    break;
                                }
                            }
                        }
                    }
                    if (toolName != null) break;
                }

                // Fallback: if all params are strings, still test with a required param + null
                if (toolName == null)
                {
                    foreach (var tool in tools.EnumerateArray())
                    {
                        if (tool.TryGetProperty("name", out var name) &&
                            tool.TryGetProperty("inputSchema", out var schema) &&
                            schema.TryGetProperty("required", out var required) &&
                            required.ValueKind == JsonValueKind.Array &&
                            required.GetArrayLength() > 0)
                        {
                            toolName = name.GetString();
                            targetParam = required[0].GetString();
                            expectedType = "required-field";
                            break;
                        }
                    }
                }
            }
        }
        catch { /* use null */ }

        if (toolName == null)
        {
            return CreateSkippedResult("Skipped: No suitable tool found for hallucination fuzzing.", "No typed parameters discovered.", "Low", CollectProbeContexts(toolsResponse.ProbeContext));
        }

        // 3. Send type-mismatched payload
        object mismatchedValue = expectedType switch
        {
            "integer" or "number" => "not-a-number",      // string where int expected
            "boolean" => "not-a-bool",                      // string where bool expected
            "array" => "not-an-array",                      // string where array expected
            "object" => "not-an-object",                    // string where object expected
            "required-field" => (object)null!,              // null for required field
            _ => 12345                                       // int where string expected
        };

        var args = new Dictionary<string, object?> { { targetParam!, mismatchedValue } };
        var response = await client.CallAsync(
            serverConfig.Endpoint!,
            ValidationConstants.Methods.ToolsCall,
            new { name = toolName, arguments = args },
            serverConfig.Authentication,
            cancellationToken);

        // 4. Grade the error response
        var clarityScore = GradeErrorClarity(response, targetParam!, expectedType!);

        var analysis = clarityScore >= 70
            ? $"Error clarity: {clarityScore}/100 (Good). Server error messages help AI self-correct."
            : clarityScore >= 40
                ? $"Error clarity: {clarityScore}/100 (Fair). Error messages partially helpful for AI."
                : $"Error clarity: {clarityScore}/100 (Poor). Error messages do not help AI fix mistakes.";

        var evidence = $"Tool: {toolName}, Param: {targetParam}, Expected: {expectedType}, Sent: {mismatchedValue?.GetType().Name ?? "null"}, " +
                       $"Response: {(response.Error ?? response.RawJson ?? "empty")?.Substring(0, Math.Min(200, (response.Error ?? response.RawJson ?? "empty").Length))}";

        // This test doesn't "fail" the server — it's informational for AI readiness
        return CreateResult(true, analysis, evidence, clarityScore < 40 ? "Medium" : "Low", probeContexts: CollectProbeContexts(toolsResponse.ProbeContext, response.ProbeContext));
    }

    /// <summary>
    /// Grades the error message on a 0-100 scale for AI agent self-correction helpfulness.
    /// Criteria:
    /// - Does the error mention the parameter name? (+30)
    /// - Does it mention the expected type? (+25)
    /// - Does it mention the actual/received value? (+15)
    /// - Is it a structured JSON-RPC error (not a 500 crash)? (+20)
    /// - Is the message non-empty and specific (>20 chars)? (+10)
    /// </summary>
    private int GradeErrorClarity(JsonRpcResponse response, string paramName, string expectedType)
    {
        int score = 0;
        var text = (response.Error ?? response.RawJson ?? "").ToLowerInvariant();

        // Structured error (not a crash)
        if (response.StatusCode != 500 && (response.StatusCode == 400 || text.Contains("error")))
            score += 20;

        // Mentions the parameter name
        if (text.Contains(paramName.ToLowerInvariant()))
            score += 30;

        // Mentions expected type
        if (text.Contains(expectedType.ToLowerInvariant()) || text.Contains("type") || text.Contains("expected"))
            score += 25;

        // Mentions actual/received value
        if (text.Contains("received") || text.Contains("actual") || text.Contains("got") || text.Contains("invalid"))
            score += 15;

        // Non-trivial message length (not just "Error" or "Internal Server Error")
        if (text.Length > 20)
            score += 10;

        return Math.Min(100, score);
    }
}
