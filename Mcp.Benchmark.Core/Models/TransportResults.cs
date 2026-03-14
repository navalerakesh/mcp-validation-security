using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Describes transport-level metadata that accompanies validation calls.
/// Abstracting this data lets us swap the implementation once the SDK exposes HTTP details natively.
/// </summary>
public sealed class TransportMetadata
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public static TransportMetadata Empty { get; } = new TransportMetadata
    {
        StatusCode = null,
        Headers = EmptyHeaders,
        Duration = TimeSpan.Zero,
        RawContent = null
    };

    public int? StatusCode { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = EmptyHeaders;
    public TimeSpan Duration { get; init; }
    public string? RawContent { get; init; }
}

/// <summary>
/// Generic container that combines the SDK payload with the associated transport metadata.
/// </summary>
public sealed class TransportResult<TPayload>
{
    public bool IsSuccessful { get; init; }
    public string? Error { get; init; }
    public TPayload? Payload { get; init; }
    public TransportMetadata Transport { get; init; } = TransportMetadata.Empty;
}

/// <summary>
/// Summary of capability validation that combines SDK tool data with legacy scoring expectations.
/// </summary>
public sealed class CapabilitySummary
{
    public IReadOnlyList<McpClientTool> Tools { get; init; } = Array.Empty<McpClientTool>();
    public bool ToolListingSucceeded { get; init; }
    public bool ToolInvocationSucceeded { get; init; }
    public string? FirstToolName { get; init; }
    public int DiscoveredToolsCount { get; init; }
    public double Score { get; init; }
    public JsonRpcResponse? ToolListResponse { get; init; }
    public JsonRpcResponse? ResourceListResponse { get; init; }
    public JsonRpcResponse? PromptListResponse { get; init; }
    public double ToolListDurationMs { get; init; }
    public double ResourceListDurationMs { get; init; }
    public double PromptListDurationMs { get; init; }
    public bool ResourceListingSucceeded { get; init; }
    public bool PromptListingSucceeded { get; init; }
    public int DiscoveredResourcesCount { get; init; }
    public int DiscoveredPromptsCount { get; init; }
}
