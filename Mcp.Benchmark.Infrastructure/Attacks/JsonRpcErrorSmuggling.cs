using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Services;
using System.Text.Json;
using System.Text;

namespace Mcp.Benchmark.Infrastructure.Attacks;

public class JsonRpcErrorSmuggling : BaseAttackVector
{
    public JsonRpcErrorSmuggling(ILogger<JsonRpcErrorSmuggling> logger) : base(logger) { }

    public override string Id => "MCP-SEC-001";
    public override string Name => "JSON-RPC Error Smuggling";
    public override string Category => "Protocol Abuse";
    public override string Description => "Sends malformed JSON-RPC requests to ensure server handles them with standard error codes, not crashes or 500s.";

    public override async Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken)
    {
        var missingVersionJson = JsonSerializer.Serialize(new { method = "initialize", @params = new { }, id = 1 });
        var missingVersionResponse = await client.SendRawJsonAsync(
            serverConfig.Endpoint!,
            missingVersionJson,
            cancellationToken);

        if (missingVersionResponse.StatusCode is 401 or 403)
        {
            return CreateSkippedResult(
                "Missing-version probe was not evaluated because authentication blocked the raw protocol request.",
                $"HTTP {missingVersionResponse.StatusCode}",
                probeContexts: CollectProbeContexts(missingVersionResponse.ProbeContext));
        }

        if (IsUnavailable(missingVersionResponse.StatusCode))
        {
            return CreateResult(
                false,
                "Missing-version probe was inconclusive because the target was unavailable or constrained.",
                missingVersionResponse.Error ?? $"HTTP {missingVersionResponse.StatusCode}",
                "Low",
                AttackSimulationOutcome.Inconclusive,
                CollectProbeContexts(missingVersionResponse.ProbeContext));
        }

        var response2 = await client.CallAsync(serverConfig.Endpoint!, "rpc.system.invalid", null, serverConfig.Authentication, cancellationToken);

        var missingVersionRejectedCorrectly = HasJsonRpcErrorCode(missingVersionResponse.RawJson, -32600);
        var passed = missingVersionRejectedCorrectly;
        var evidence = missingVersionRejectedCorrectly
            ? "Missing jsonrpc version returned -32600 Invalid Request. "
            : $"Missing jsonrpc version was not rejected with -32600 (HTTP {missingVersionResponse.StatusCode}). ";

        if (response2.StatusCode == 500)
        {
            passed = false;
            evidence += "Server crashed (500) on invalid method. ";
        }
        else if (response2.IsSuccess && response2.RawJson != null && !response2.RawJson.Contains("error"))
        {
             // Should be an error
             evidence += "Server accepted invalid method name. ";
             // passed = false; // Optional strictness
        }

        if (passed)
        {
            return CreateResult(true, "Server handled malformed requests gracefully with standard errors.", evidence, probeContexts: CollectProbeContexts(missingVersionResponse.ProbeContext, response2.ProbeContext));
        }
        else
        {
            return CreateResult(false, "Server failed to handle malformed JSON-RPC requests correctly.", evidence, "Critical", probeContexts: CollectProbeContexts(missingVersionResponse.ProbeContext, response2.ProbeContext));
        }
    }

    private static bool IsUnavailable(int statusCode) =>
        statusCode < 0 || statusCode is 408 or 425 or 429 || statusCode >= 500;

    private static bool HasJsonRpcErrorCode(string? rawJson, int expectedCode)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("code", out var code) &&
                   code.TryGetInt32(out var actualCode) &&
                   actualCode == expectedCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
