using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class BoundedArtifactReader
{
    internal const int MaximumValidationResultBytes = 16 * 1024 * 1024;
    internal const int MaximumConfigurationBytes = 1024 * 1024;
    internal const int MaximumProviderHarnessBytes = 4 * 1024 * 1024;
    internal const int MaximumPublicKeyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        MaxDepth = 64
    };

    internal static async Task<McpValidatorConfiguration> ReadConfigurationAsync(
        FileInfo file,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadTextAsync(file, MaximumConfigurationBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = ConfigurationOptions.MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new CliUsageException("Configuration must be a JSON object.");

            ValidateConfigurationValue(document.RootElement, typeof(McpValidatorConfiguration), "$", new NullabilityInfoContext());
            return JsonSerializer.Deserialize<McpValidatorConfiguration>(json, ConfigurationOptions)
                ?? throw new CliUsageException("Configuration must not be null.");
        }
        catch (JsonException)
        {
            throw new CliUsageException("Configuration contains invalid JSON or a value incompatible with the configuration contract.");
        }
    }

    private static void ValidateConfigurationValue(JsonElement value, Type type, string location, NullabilityInfoContext nullability)
    {
        var contract = ConfigurationOptions.GetTypeInfo(type);
        if (value.ValueKind == JsonValueKind.Object)
        {
            var isContract = contract.Kind == JsonTypeInfoKind.Object;
            var names = new HashSet<string>(isContract || location is "$.server.Headers" or "$.server.authentication.customHeaders"
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var dictionaryType = type.GetInterfaces().Append(type)
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            var valueType = dictionaryType?.GetGenericArguments()[1] ?? typeof(object);
            foreach (var member in value.EnumerateObject())
            {
                if (!names.Add(member.Name))
                    throw new CliUsageException($"Configuration contains a duplicate property at {location}.");

                if (isContract)
                {
                    var property = contract.Properties.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                    if (property is null)
                        throw new CliUsageException($"Configuration contains an unknown property at {location}.");
                    var propertyLocation = $"{location}.{property.Name}";
                    if (member.Value.ValueKind == JsonValueKind.Null && property.AttributeProvider is PropertyInfo propertyInfo &&
                        nullability.Create(propertyInfo).WriteState == NullabilityState.NotNull)
                        throw new CliUsageException($"Configuration property {propertyLocation} must not be null.");
                    ValidateConfigurationValue(member.Value, property.PropertyType, propertyLocation, nullability);
                }
                else
                {
                    if (member.Value.ValueKind == JsonValueKind.Null && valueType == typeof(string))
                        throw new CliUsageException($"Configuration contains a null dictionary value at {location}.");
                    ValidateConfigurationValue(member.Value, valueType, $"{location}.*", nullability);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var enumerableType = type.GetInterfaces().Append(type)
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            var elementType = enumerableType?.GetGenericArguments()[0] ?? typeof(object);
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Null && elementType != typeof(object) && elementType != typeof(JsonElement))
                    throw new CliUsageException($"Configuration contains a null collection item at {location}.");
                ValidateConfigurationValue(element, elementType, $"{location}[]", nullability);
            }
        }
    }

    internal static async Task<ProviderHarnessManifest> ReadProviderHarnessAsync(
        FileInfo file,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadTextAsync(file, MaximumProviderHarnessBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("documentType", out var documentType) ||
                !root.TryGetProperty("documentSchemaVersion", out var schemaVersion) ||
                !string.Equals(documentType.GetString(), ArtifactContracts.ProviderHarnessDocumentType, StringComparison.Ordinal) ||
                !ArtifactContracts.IsCompatible(ArtifactContracts.ProviderHarnessDocumentType, schemaVersion.GetString() ?? string.Empty))
            {
                throw new CliUsageException("Provider harness artifact has an incompatible document type or schema version.");
            }

            return JsonSerializer.Deserialize<ProviderHarnessManifest>(
                json,
                ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true))
                ?? throw new CliUsageException("Provider harness artifact was empty.");
        }
        catch (JsonException exception)
        {
            throw new CliUsageException("Provider harness artifact is not valid JSON for the supported contract.", exception);
        }
    }

    internal static async Task<ValidationResult> ReadValidationResultAsync(
        FileInfo file,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadTextAsync(file, MaximumValidationResultBytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 128,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("documentType", out var documentType) || documentType.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("documentSchemaVersion", out var schemaVersion) || schemaVersion.ValueKind != JsonValueKind.String)
        {
            throw new CliUsageException("Validation artifact is missing its document type or schema version.");
        }

        var type = documentType.GetString();
        var version = schemaVersion.GetString();
        if (!string.Equals(type, ArtifactContracts.ValidationResultDocumentType, StringComparison.Ordinal) ||
            !ArtifactContracts.IsCompatible(type!, version!))
        {
            throw new CliUsageException($"Validation artifact contract '{type}' version '{version}' is not compatible with this validator.");
        }

        return JsonSerializer.Deserialize<ValidationResult>(
            RemoveRuntimeOnlyFields(json),
            ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true))
            ?? throw new CliUsageException("Validation artifact did not contain a validation result.");
    }

    private static string RemoveRuntimeOnlyFields(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            MaxDepth = 128,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (root?["run"]?["capabilitySnapshot"]?["payload"] is JsonObject payload &&
            payload["tools"] is JsonArray { Count: > 0 })
        {
            payload["tools"] = new JsonArray();
        }

        return root?.ToJsonString() ?? json;
    }

    internal static async Task<string> ReadTextAsync(
        FileInfo file,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) throw new CliUsageException($"Input file was not found: {file.FullName}");
        if (file.LinkTarget != null) throw new CliUsageException("Input files must be regular files, not symbolic links.");
        if (file.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        var bytes = new byte[maximumBytes + 1];
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        if (totalRead > maximumBytes || stream.ReadByte() != -1)
        {
            throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        }
        return new UTF8Encoding(false, true).GetString(bytes, 0, totalRead);
    }

    internal static string ReadText(FileInfo file, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) throw new CliUsageException($"Input file was not found: {file.FullName}");
        if (file.LinkTarget != null) throw new CliUsageException("Input files must be regular files, not symbolic links.");
        if (file.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var bytes = new byte[maximumBytes + 1];
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
            if (read == 0) break;
            totalRead += read;
        }
        if (totalRead > maximumBytes || stream.ReadByte() != -1) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, totalRead);
    }
}