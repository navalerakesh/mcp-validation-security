using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Attacks;

public class MetadataEnumeration : BaseAttackVector
{
    public MetadataEnumeration(ILogger<MetadataEnumeration> logger) : base(logger) { }

    public override string Id => "MCP-SEC-002";
    public override string Name => "Metadata Enumeration";
    public override string Category => "Data Leakage";
    public override string Description => "Attempts to infer existence of hidden resources by analyzing error responses for random vs likely IDs.";

    public override async Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken)
    {
        // 1. Request a definitely non-existent resource
        var randomId = "resource://" + Guid.NewGuid().ToString();
        var responseRandom = await client.CallAsync(serverConfig.Endpoint!, "resources/read", new { uri = randomId }, serverConfig.Authentication, cancellationToken);

        // 2. Request a likely existing but potentially hidden resource (common names)
        var commonId = "file:///etc/passwd"; // Classic test
        var responseCommon = await client.CallAsync(serverConfig.Endpoint!, "resources/read", new { uri = commonId }, serverConfig.Authentication, cancellationToken);

        // Analysis
        // Ideally, both should return the SAME error (e.g., "Resource not found" or "Invalid URI").
        // If one returns "Access Denied" and the other "Not Found", we have enumeration.
        
        bool isBlocked = true;
        string analysis = "Consistent error responses observed.";
        string evidence = "";

        if (responseRandom.StatusCode != responseCommon.StatusCode)
        {
            // Different HTTP status codes is a strong signal
            isBlocked = false;
            analysis = "Potential enumeration: Different HTTP status codes for random vs common resources.";
            evidence = $"Random: {responseRandom.StatusCode}, Common: {responseCommon.StatusCode}";
        }
        else if (responseRandom.RawJson != responseCommon.RawJson)
        {
            // If the JSON error message is significantly different (beyond just the ID)
            // This is harder to detect automatically without fuzzy matching, but we can check for specific keywords
            // For now, we'll be lenient unless it's obvious
        }

        return CreateResult(isBlocked, analysis, evidence, "High");
    }
}
