using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Attacks;

public class MetadataEnumeration : BaseAttackVector
{
    private const string CommonProbeName = "config";

    public MetadataEnumeration(ILogger<MetadataEnumeration> logger) : base(logger) { }

    public override string Id => "MCP-SEC-002";
    public override string Name => "Metadata Enumeration";
    public override string Category => "Data Leakage";
    public override string Description => "Attempts to infer existence of hidden resources by comparing same-family resource probes on an advertised resource surface.";

    public override async Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken)
    {
        var advertisedResourceUri = await TryGetAdvertisedResourceUriAsync(serverConfig, client, cancellationToken);
        if (string.IsNullOrWhiteSpace(advertisedResourceUri))
        {
            return CreateResult(
                isBlocked: true,
                analysis: "Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.",
                evidence: "resources/list did not advertise any concrete resource URIs, so cross-scheme fallback probes were suppressed to avoid false positives.",
                severity: "Low");
        }

        var randomProbeUri = BuildSiblingProbeUri(advertisedResourceUri, $"__mcpval_probe_{Guid.NewGuid():N}");
        var commonProbeUri = BuildSiblingProbeUri(advertisedResourceUri, CommonProbeName);

        var responseRandom = await client.CallAsync(
            serverConfig.Endpoint!,
            ValidationConstants.Methods.ResourcesRead,
            new { uri = randomProbeUri },
            serverConfig.Authentication,
            cancellationToken);

        var responseCommon = await client.CallAsync(
            serverConfig.Endpoint!,
            ValidationConstants.Methods.ResourcesRead,
            new { uri = commonProbeUri },
            serverConfig.Authentication,
            cancellationToken);

        var randomClassification = Classify(responseRandom);
        var commonClassification = Classify(responseCommon);
        var evidence = $"Advertised family seed: {advertisedResourceUri}; Random probe {randomProbeUri} => {randomClassification} ({responseRandom.StatusCode}); Common-name probe {commonProbeUri} => {commonClassification} ({responseCommon.StatusCode})";

        if (IndicatesEnumeration(randomClassification, commonClassification))
        {
            return CreateResult(
                isBlocked: false,
                analysis: "Potential enumeration within an advertised resource family: the common-name probe resolved differently from the random probe.",
                evidence: evidence,
                severity: "Medium");
        }

        return CreateResult(
            isBlocked: true,
            analysis: "Consistent same-family error handling observed for unadvertised resource probes.",
            evidence: evidence,
            severity: "Low");
    }

    private static async Task<string?> TryGetAdvertisedResourceUriAsync(
        McpServerConfig serverConfig,
        IMcpHttpClient client,
        CancellationToken cancellationToken)
    {
        var resourcesResponse = await client.CallAsync(
            serverConfig.Endpoint!,
            ValidationConstants.Methods.ResourcesList,
            null,
            serverConfig.Authentication,
            cancellationToken);

        if (!resourcesResponse.IsSuccess || string.IsNullOrWhiteSpace(resourcesResponse.RawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(resourcesResponse.RawJson);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                if (resource.TryGetProperty("uri", out var uriElement) &&
                    uriElement.ValueKind == JsonValueKind.String)
                {
                    var uri = uriElement.GetString();
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        return uri;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string BuildSiblingProbeUri(string advertisedResourceUri, string probeName)
    {
        if (Uri.TryCreate(advertisedResourceUri, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

            if (!string.IsNullOrEmpty(builder.Path) && builder.Path != "/")
            {
                var path = builder.Path;
                if (path.EndsWith("/", StringComparison.Ordinal))
                {
                    builder.Path = path + probeName;
                }
                else
                {
                    var lastSlash = path.LastIndexOf('/');
                    builder.Path = lastSlash >= 0
                        ? string.Concat(path.AsSpan(0, lastSlash + 1), probeName)
                        : "/" + probeName;
                }

                return builder.Uri.ToString();
            }

            if (!string.IsNullOrEmpty(builder.Host))
            {
                builder.Path = "/" + probeName;
                return builder.Uri.ToString();
            }
        }

        var trimmed = advertisedResourceUri.TrimEnd('/');
        var trimmedLastSlash = trimmed.LastIndexOf('/');
        if (trimmedLastSlash >= 0)
        {
            return $"{trimmed[..(trimmedLastSlash + 1)]}{probeName}";
        }

        var schemeSeparator = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            return $"{trimmed}/{probeName}";
        }

        var colonIndex = trimmed.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            return $"{trimmed[..(colonIndex + 1)]}{probeName}";
        }

        return probeName;
    }

    private static ProbeClassification Classify(JsonRpcResponse response)
    {
        if (response.IsSuccess)
        {
            return ProbeClassification.Success;
        }

        if (response.StatusCode is 401 or 403)
        {
            return ProbeClassification.AccessDenied;
        }

        if (response.StatusCode == 404)
        {
            return ProbeClassification.NotFound;
        }

        var content = string.Concat(response.RawJson, " ", response.Error).ToLowerInvariant();
        if (content.Contains("forbidden", StringComparison.Ordinal) ||
            content.Contains("unauthorized", StringComparison.Ordinal) ||
            content.Contains("access denied", StringComparison.Ordinal) ||
            content.Contains("permission", StringComparison.Ordinal))
        {
            return ProbeClassification.AccessDenied;
        }

        if (content.Contains("not found", StringComparison.Ordinal) ||
            content.Contains("unknown resource", StringComparison.Ordinal) ||
            content.Contains("does not exist", StringComparison.Ordinal))
        {
            return ProbeClassification.NotFound;
        }

        if (response.StatusCode == 400 ||
            content.Contains("invalid", StringComparison.Ordinal) ||
            content.Contains("parse", StringComparison.Ordinal) ||
            content.Contains("-32602", StringComparison.Ordinal))
        {
            return ProbeClassification.Invalid;
        }

        return ProbeClassification.Other;
    }

    private static bool IndicatesEnumeration(ProbeClassification randomClassification, ProbeClassification commonClassification)
        => randomClassification is ProbeClassification.NotFound or ProbeClassification.Invalid
           && commonClassification is ProbeClassification.AccessDenied or ProbeClassification.Success;

    private enum ProbeClassification
    {
        Success,
        AccessDenied,
        NotFound,
        Invalid,
        Other
    }
}
