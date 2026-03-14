using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using System.Text.Json;

namespace Mcp.Benchmark.Infrastructure.Attacks;

public class SchemaConfusion : BaseAttackVector
{
    public SchemaConfusion(ILogger<SchemaConfusion> logger) : base(logger) { }

    public override string Id => "MCP-SEC-003";
    public override string Name => "Schema Confusion";
    public override string Category => "Tool Boundary";
    public override string Description => "Sends invalid data types (arrays vs objects, nulls) to tool arguments to test validation resilience.";

    public override async Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken)
    {
        // We need to find a tool to test first.
        var toolsResponse = await client.CallAsync(serverConfig.Endpoint!, "tools/list", new { }, serverConfig.Authentication, cancellationToken);
        
        if (!toolsResponse.IsSuccess || string.IsNullOrEmpty(toolsResponse.RawJson))
        {
            return CreateResult(true, "Skipped: Could not list tools to test.", "No tools available.");
        }

        string toolName = "test-tool";
        try 
        {
            using var doc = JsonDocument.Parse(toolsResponse.RawJson);
            if (doc.RootElement.TryGetProperty("result", out var result) && 
                result.TryGetProperty("tools", out var tools) && 
                tools.GetArrayLength() > 0)
            {
                toolName = tools[0].GetProperty("name").GetString() ?? "test-tool";
            }
        }
        catch { /* use default */ }

        // Attack: Send an array as arguments instead of an object
        var response = await client.CallAsync(serverConfig.Endpoint!, "tools/call", 
            new { name = toolName, arguments = new[] { "invalid", "array" } }, 
            serverConfig.Authentication, cancellationToken);

        // Analysis
        if (response.StatusCode == 500)
        {
            return CreateResult(false, "Server crashed (500) on schema mismatch.", "Sent array as arguments, got 500.", "High");
        }

        // Should be an error
        if (response.IsSuccess && response.RawJson != null && !response.RawJson.Contains("error"))
        {
             // If it returned success, it might have ignored the arguments (which is okay-ish) or misbehaved
             // But strictly, arguments MUST be an object.
             // We'll check if it's a JSON-RPC error
             return CreateResult(false, "Server accepted invalid schema (array instead of object).", "Response was success.", "Medium");
        }

        return CreateResult(true, "Server correctly rejected invalid schema.", "Response contained error or 400.");
    }
}
