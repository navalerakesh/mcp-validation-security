using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using InitializeResult = ModelContextProtocol.Protocol.InitializeResult;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class CapabilitySnapshotUtils
{
    private static readonly JsonSerializerOptions CapabilityJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonRpcResponse? CloneResponse(JsonRpcResponse? source)
    {
        if (source == null)
        {
            return null;
        }

        return new JsonRpcResponse
        {
            StatusCode = source.StatusCode,
            IsSuccess = source.IsSuccess,
            RawJson = source.RawJson,
            Error = source.Error,
            Headers = source.Headers != null
                ? new Dictionary<string, string>(source.Headers)
                : new Dictionary<string, string>()
        };
    }

    public static bool HasCapabilityDeclarations(InitializeResult? initializeResult) => initializeResult?.Capabilities != null;

    public static bool HasCapabilityDeclarations(TransportResult<CapabilitySummary>? snapshot) =>
        snapshot?.Payload?.CapabilityDeclarationsAvailable == true;

    public static bool IsCapabilityAdvertised(TransportResult<CapabilitySummary>? snapshot, string capability) =>
        snapshot?.Payload?.AdvertisedCapabilities.Any(value => string.Equals(value, capability, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool IsCapabilityExplicitlyNotAdvertised(TransportResult<CapabilitySummary>? snapshot, string capability) =>
        HasCapabilityDeclarations(snapshot) && !IsCapabilityAdvertised(snapshot, capability);

    public static bool ShouldProbeCapability(TransportResult<CapabilitySummary>? snapshot, string capability) =>
        !HasCapabilityDeclarations(snapshot) || IsCapabilityAdvertised(snapshot, capability);

    public static bool ShouldProbeCapability(
        bool capabilityDeclarationsAvailable,
        IEnumerable<string>? advertisedCapabilities,
        string capability)
    {
        return !capabilityDeclarationsAvailable ||
            advertisedCapabilities?.Any(value => string.Equals(value, capability, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public static IReadOnlyList<string> ExtractAdvertisedCapabilities(InitializeResult? initializeResult)
    {
        if (initializeResult?.Capabilities == null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var capabilitiesJson = JsonSerializer.Serialize(initializeResult.Capabilities, CapabilityJsonOptions);
            using var document = JsonDocument.Parse(capabilitiesJson);
            return ExtractAdvertisedCapabilities(document.RootElement);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> ExtractAdvertisedCapabilities(JsonElement capabilities)
    {
        var advertisedCapabilities = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (capabilities.ValueKind != JsonValueKind.Object)
        {
            return advertisedCapabilities.ToList();
        }

        AddRootCapability(
            capabilities,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Tools,
            ("listChanged", McpSpecConstants.Capabilities.ToolsListChanged));

        AddRootCapability(
            capabilities,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Resources,
            ("subscribe", McpSpecConstants.Capabilities.ResourcesSubscribe),
            ("listChanged", McpSpecConstants.Capabilities.ResourcesListChanged));

        AddRootCapability(
            capabilities,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Prompts,
            ("listChanged", McpSpecConstants.Capabilities.PromptsListChanged));

        AddRootCapability(capabilities, advertisedCapabilities, McpSpecConstants.Capabilities.Logging);
        AddRootCapability(capabilities, advertisedCapabilities, McpSpecConstants.Capabilities.Completions);
        AddRootCapability(capabilities, advertisedCapabilities, McpSpecConstants.Capabilities.Roots);

        if (TryGetPropertyIgnoreCase(capabilities, McpSpecConstants.Capabilities.Sampling, out var samplingCapability))
        {
            advertisedCapabilities.Add(McpSpecConstants.Capabilities.Sampling);
            AddNestedCapabilityLeaves(samplingCapability, McpSpecConstants.Capabilities.Sampling, advertisedCapabilities);
        }

        if (TryGetPropertyIgnoreCase(capabilities, McpSpecConstants.Capabilities.Elicitation, out var elicitationCapability))
        {
            advertisedCapabilities.Add(McpSpecConstants.Capabilities.Elicitation);
            var properties = elicitationCapability.ValueKind == JsonValueKind.Object
                ? elicitationCapability.EnumerateObject().ToArray()
                : Array.Empty<JsonProperty>();
            if (properties.Length == 0)
            {
                advertisedCapabilities.Add(McpSpecConstants.Capabilities.ElicitationForm);
            }

            AddNestedCapabilityLeaves(elicitationCapability, McpSpecConstants.Capabilities.Elicitation, advertisedCapabilities);
        }

        if (TryGetPropertyIgnoreCase(capabilities, McpSpecConstants.Capabilities.Tasks, out var tasksCapability))
        {
            advertisedCapabilities.Add(McpSpecConstants.Capabilities.Tasks);
            AddNestedCapabilityLeaves(tasksCapability, McpSpecConstants.Capabilities.Tasks, advertisedCapabilities);
        }

        return advertisedCapabilities.ToList();
    }

    private static void AddRootCapability(
        JsonElement capabilities,
        ISet<string> advertisedCapabilities,
        string rootCapability,
        params (string PropertyName, string CapabilityName)[] booleanSubCapabilities)
    {
        if (!TryGetPropertyIgnoreCase(capabilities, rootCapability, out var capabilityValue))
        {
            return;
        }

        advertisedCapabilities.Add(rootCapability);
        if (capabilityValue.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var (propertyName, capabilityName) in booleanSubCapabilities)
        {
            if (TryGetPropertyIgnoreCase(capabilityValue, propertyName, out var subCapabilityValue) &&
                subCapabilityValue.ValueKind == JsonValueKind.True)
            {
                advertisedCapabilities.Add(capabilityName);
            }
        }
    }

    private static void AddNestedCapabilityLeaves(JsonElement element, string path, ISet<string> advertisedCapabilities)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var nextPath = $"{path}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.True)
            {
                advertisedCapabilities.Add(nextPath);
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (!property.Value.EnumerateObject().Any())
                {
                    advertisedCapabilities.Add(nextPath);
                }

                AddNestedCapabilityLeaves(property.Value, nextPath, advertisedCapabilities);
            }
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
