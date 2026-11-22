using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using System.Reflection;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal sealed record ConfigurationDocument(McpValidatorConfiguration Configuration, IReadOnlySet<string> SuppliedSettings);

internal static class BoundedArtifactReader
{
    internal const int MaximumValidationResultBytes = 16 * 1024 * 1024;
    internal const int MaximumConfigurationBytes = 1024 * 1024;
    internal const int MaximumProviderHarnessBytes = 4 * 1024 * 1024;
    internal const int MaximumPublicKeyBytes = 64 * 1024;
    internal const string ConfigurationSchemaVersion = "1.0.0";
    private const string ConfigurationSchemaVersionProperty = "schemaVersion";

    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                contract =>
                {
                    if (contract.Type == typeof(McpValidatorConfiguration))
                        contract.Properties.Add(contract.CreateJsonPropertyInfo(typeof(string), ConfigurationSchemaVersionProperty));
                }
            }
        },
        MaxDepth = 64
    };

    internal static JsonNode CreateConfigurationSchema() => ConfigurationOptions.GetJsonSchemaAsNode(
        typeof(McpValidatorConfiguration), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, schema) =>
            {
                var enumType = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
                if (enumType.IsEnum)
                {
                    var choices = new JsonArray();
                    if (schema is JsonObject enumSchema && enumSchema["enum"] is JsonArray names)
                    {
                        foreach (var name in names.Where(name => name?.GetValueKind() == JsonValueKind.String))
                            choices.Add(new JsonObject
                            {
                                ["type"] = "string",
                                ["pattern"] = "^\\s*" + CaseInsensitivePattern(name!.GetValue<string>()) + "\\s*$(?![\\s\\S])"
                            });
                    }
                    foreach (var selection in Enum.GetValues(enumType).Cast<object>().Distinct())
                        choices.Add(new JsonObject
                        {
                            ["const"] = JsonSerializer.SerializeToNode(Convert.ChangeType(selection, Enum.GetUnderlyingType(enumType),
                                System.Globalization.CultureInfo.InvariantCulture))
                        });
                    if (Nullable.GetUnderlyingType(context.TypeInfo.Type) is not null)
                        choices.Add(new JsonObject { ["type"] = "null" });
                    return new JsonObject { ["anyOf"] = choices };
                }
                if (context.TypeInfo.Kind != JsonTypeInfoKind.Object || schema is not JsonObject schemaObject ||
                    schemaObject["properties"] is not JsonObject properties)
                    return schema;

                if (context.TypeInfo.Type == typeof(McpValidatorConfiguration))
                {
                    properties[ConfigurationSchemaVersionProperty] = new JsonObject
                    {
                        ["type"] = "string",
                        ["const"] = ConfigurationSchemaVersion
                    };
                    schemaObject["$schema"] = "https://json-schema.org/draft/2020-12/schema";
                }

                var aliases = new JsonObject();
                foreach (var property in properties)
                {
                    var pattern = "^" + CaseInsensitivePattern(property.Key) + "$(?![\\s\\S])";
                    var pointer = "#/" + string.Join("/", context.Path.ToArray().Concat(["properties", property.Key])
                        .Select(segment => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
                    aliases[pattern] = new JsonObject { ["$ref"] = pointer };
                }
                schemaObject["patternProperties"] = aliases;
                return schema;
            }
        });

    private static string CaseInsensitivePattern(string value) => string.Concat(value.Select(character => char.IsAsciiLetter(character)
        ? $"[{char.ToLowerInvariant(character)}{char.ToUpperInvariant(character)}]"
        : Regex.Escape(character.ToString())));

    internal static async Task<McpValidatorConfiguration> ReadConfigurationAsync(
        FileInfo file,
        CancellationToken cancellationToken = default) =>
        (await ReadConfigurationDocumentAsync(file, cancellationToken).ConfigureAwait(false)).Configuration;

    internal static async Task<ConfigurationDocument> ReadConfigurationDocumentAsync(
        FileInfo file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await ReadTextAsync(file, MaximumConfigurationBytes, cancellationToken).ConfigureAwait(false);
            if (json.StartsWith('\uFEFF')) json = json[1..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = ConfigurationOptions.MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new CliUsageException("Configuration must be a JSON object.");

            var suppliedSettings = new HashSet<string>(StringComparer.Ordinal);
            ValidateConfigurationValue(document.RootElement, typeof(McpValidatorConfiguration), "$", new NullabilityInfoContext(), suppliedSettings);
            var configuration = JsonSerializer.Deserialize<McpValidatorConfiguration>(json, ConfigurationOptions)
                ?? throw new CliUsageException("Configuration must not be null.");
            VisitEffectiveConfigurationSettings(configuration, (location, setting) =>
            {
                if (setting is Enum selection && !Enum.IsDefined(selection.GetType(), selection))
                    throw new CliUsageException($"Configuration property {location} contains an unsupported selection.");
            }, inspectCollections: true);
            return new ConfigurationDocument(configuration, suppliedSettings);
        }
            catch (DecoderFallbackException)
            {
                throw new CliUsageException("Configuration must contain valid UTF-8 text.");
            }
        catch (JsonException)
        {
            throw new CliUsageException("Configuration contains invalid JSON or a value incompatible with the configuration contract.");
        }
    }

    internal static IReadOnlyList<string> GetEffectiveConfigurationSettingPaths(McpValidatorConfiguration configuration)
    {
        var paths = new List<string>();
        VisitEffectiveConfigurationSettings(configuration, (location, _) => paths.Add(location));
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyDictionary<string, bool> GetEffectiveBooleanSettings(McpValidatorConfiguration configuration)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal);
        VisitEffectiveConfigurationSettings(configuration, (location, value) =>
        {
            if (value is bool boolean) values.Add(location, boolean);
        });
        return values;
    }

    private static void VisitEffectiveConfigurationSettings(McpValidatorConfiguration configuration, Action<string, object?> visitor,
        bool inspectCollections = false)
    {
        Collect(configuration, typeof(McpValidatorConfiguration), "$", 0);

        void Collect(object? value, Type type, string location, int depth)
        {
            if (depth > ConfigurationOptions.MaxDepth)
                throw new CliUsageException("Configuration exceeds the supported nesting depth.");
            var contract = ConfigurationOptions.GetTypeInfo(type);
            if (value is not null && contract.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in contract.Properties.Where(property => property.Get is not null))
                    Collect(property.Get!(value), property.PropertyType, $"{location}.{property.Name}", depth + 1);
            }
            else if (inspectCollections && value is System.Collections.IDictionary dictionary)
            {
                foreach (var element in dictionary.Values)
                    Collect(element, element?.GetType() ?? typeof(object), $"{location}.*", depth + 1);
            }
            else if (inspectCollections && contract.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary &&
                value is System.Collections.IEnumerable elements)
            {
                foreach (var element in elements)
                    Collect(element, element?.GetType() ?? typeof(object), $"{location}[]", depth + 1);
            }
            else
                visitor(location, value);
        }
    }

    private static void ValidateConfigurationValue(JsonElement value, Type type, string location, NullabilityInfoContext nullability,
        ISet<string>? suppliedSettings = null)
    {
        var contract = ConfigurationOptions.GetTypeInfo(type);
        if (contract.Kind != JsonTypeInfoKind.Object || value.ValueKind != JsonValueKind.Object)
            suppliedSettings?.Add(location);
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
                    if (location == "$" && property.Name == ConfigurationSchemaVersionProperty &&
                        (member.Value.ValueKind != JsonValueKind.String || member.Value.GetString() != ConfigurationSchemaVersion))
                        throw new CliUsageException($"Configuration schemaVersion must be {ConfigurationSchemaVersion}; migrate unsupported versions before validation.");
                    var propertyLocation = $"{location}.{property.Name}";
                    if (member.Value.ValueKind == JsonValueKind.Null && property.AttributeProvider is PropertyInfo propertyInfo &&
                        nullability.Create(propertyInfo).WriteState == NullabilityState.NotNull)
                        throw new CliUsageException($"Configuration property {propertyLocation} must not be null.");
                    ValidateConfigurationValue(member.Value, property.PropertyType, propertyLocation, nullability, suppliedSettings);
                }
                else
                {
                    if (member.Value.ValueKind == JsonValueKind.Null && valueType == typeof(string))
                        throw new CliUsageException($"Configuration contains a null dictionary value at {location}.");
                    ValidateConfigurationValue(member.Value, valueType, $"{location}.*", nullability, suppliedSettings);
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
                ValidateConfigurationValue(element, elementType, $"{location}[]", nullability, suppliedSettings);
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

        foreach (var name in new[] { "documentType", "documentSchemaVersion" })
            if (root.EnumerateObject().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Skip(1).Any())
                throw new CliUsageException("Validation artifact contains ambiguous document identity aliases.");

        var type = documentType.GetString();
        var version = schemaVersion.GetString();
        if (!string.Equals(type, ArtifactContracts.ValidationResultDocumentType, StringComparison.Ordinal) ||
            !ArtifactContracts.IsCompatible(type!, version!))
        {
            throw new CliUsageException($"Validation artifact contract '{type}' version '{version}' is not compatible with this validator.");
        }

        ValidateRecordedAssessmentTimes(root);
        ValidateRecordedRunTermination(root);
        ValidateRecordedRequestFailures(root);
        return JsonSerializer.Deserialize<ValidationResult>(
            NormalizeSavedResult(json, version!),
            ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true))
            ?? throw new CliUsageException("Validation artifact did not contain a validation result.");
    }

    private static void ValidateRecordedRequestFailures(JsonElement root)
    {
        try
        {
            var run = Unique(root, "run");
            if (run == null) return;
            var metrics = Unique(run.Value, "operationalMetrics");
            if (metrics == null || metrics.Value.ValueKind == JsonValueKind.Null) return;
            var failures = Unique(metrics.Value, "requestFailures");
            if (failures == null || failures.Value.ValueKind == JsonValueKind.Null) return;
            if (failures.Value.ValueKind != JsonValueKind.Array || failures.Value.GetArrayLength() > RequestFailureCount.MaximumBuckets)
                throw Invalid();
            var count = Unique(metrics.Value, "requestsFailed");
            if (count == null || !count.Value.TryGetInt32(out var expectedFailures)) throw Invalid();
            var entries = new List<RequestFailureCount>();
            foreach (var element in failures.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) throw Invalid();
                var names = element.EnumerateObject().Take(5).Select(property => property.Name).ToArray();
                if (names.Length != 4 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                    new[] { "method", "httpStatusCode", "kind", "count" }.Any(name => !names.Contains(name, StringComparer.OrdinalIgnoreCase)))
                    throw Invalid();
                var entry = element.Deserialize<RequestFailureCount>(ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true));
                if (entry == null) throw Invalid();
                var kind = Unique(element, "kind")!.Value;
                if (kind.ValueKind == JsonValueKind.String && kind.GetString() != JsonNamingPolicy.CamelCase.ConvertName(entry.Kind.ToString()))
                    throw Invalid();
                entries.Add(entry);
            }
            if (!RequestFailureCount.IsValidCollection(entries, expectedFailures)) throw Invalid();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid();
        }

        static CliUsageException Invalid() => new("Validation artifact contains invalid request failure attribution.");
        static JsonElement? Unique(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object) throw Invalid();
            var matches = parent.EnumerateObject().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length > 1) throw Invalid();
            return matches.Length == 0 ? null : matches[0].Value;
        }
    }

    private static void ValidateRecordedRunTermination(JsonElement root)
    {
        var runs = root.EnumerateObject().Where(property =>
            string.Equals(property.Name, "run", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (runs.Length != 1 || runs[0].Value.ValueKind != JsonValueKind.Object)
            throw new CliUsageException("Validation artifact requires unambiguous run metadata.");
        var outcomes = runs[0].Value.EnumerateObject().Where(property =>
            string.Equals(property.Name, "terminationOutcome", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (outcomes.Length > 1)
            throw new CliUsageException("Validation artifact contains ambiguous run termination metadata.");
        if (outcomes.Length == 0)
        {
            return;
        }
        if (outcomes[0].Value.ValueKind == JsonValueKind.Null) return;
        try
        {
            var outcome = outcomes[0].Value.Deserialize<ValidationOutcome>(ArtifactJsonOptions.Create(writeIndented: false));
            if (ValidationOutcomeTaxonomy.IsRunTermination(outcome) &&
                (outcomes[0].Value.ValueKind != JsonValueKind.String ||
                 string.Equals(outcomes[0].Value.GetString(), JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString()), StringComparison.Ordinal))) return;
        }
        catch (JsonException)
        {
            throw new CliUsageException("Validation artifact contains invalid run termination metadata.");
        }
        throw new CliUsageException("Validation artifact contains invalid run termination metadata.");
    }

    private static void ValidateRecordedAssessmentTimes(JsonElement root)
    {
        foreach (var (name, required) in new[] { ("startTime", true), ("endTime", false) })
        {
            var matches = root.EnumerateObject().Where(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length > 1)
                throw new CliUsageException("Validation artifact contains ambiguous recorded timestamps.");
            if (!required && (matches.Length == 0 || matches[0].Value.ValueKind == JsonValueKind.Null)) continue;
            if (matches.Length == 0 || matches[0].Value.ValueKind != JsonValueKind.String ||
                !matches[0].Value.TryGetDateTime(out var timestamp) || timestamp.Kind == DateTimeKind.Unspecified)
                throw new CliUsageException("Validation artifact requires well-formed recorded timestamps with explicit timezones.");
        }
    }

    private static string NormalizeSavedResult(string json, string version)
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

        if (version.StartsWith("2.", StringComparison.Ordinal) && root is JsonObject artifact)
        {
            var run = GetArtifactProperty(artifact, "run") as JsonObject
                ?? throw new CliUsageException("Validation artifact is missing run metadata.");
            if (GetArtifactProperty(run, "operationalMetrics") is JsonObject metrics)
            {
                NormalizeUnmeasuredMetrics(metrics, "targetLatencySampleCount", "targetLatencyP50Ms", "targetLatencyP95Ms", "targetLatencyP99Ms");
                NormalizeUnmeasuredMetrics(metrics, "queueTimeSampleCount", "queueTimeP95Ms");
                NormalizeUnmeasuredMetrics(metrics, "requestsCompleted", "errorRate");
                NormalizeUnmeasuredMetrics(metrics, "totalRunDurationMs", "throughputRequestsPerSecond");
            }
            if (GetArtifactProperty(artifact, "measurements") is JsonObject measurements &&
                GetArtifactProperty(measurements, "performanceTesting") is JsonObject performance &&
                GetArtifactProperty(performance, "resourceUsage") is JsonObject)
            {
                performance[FindArtifactPropertyName(performance, "resourceUsage") ?? "resourceUsage"] =
                    JsonSerializer.SerializeToNode(new ResourceUsageMetrics(), ArtifactJsonOptions.Create(writeIndented: false));
            }
            run[FindArtifactPropertyName(run, "sourceDocumentSchemaVersion") ?? "sourceDocumentSchemaVersion"] = version;
            artifact["documentSchemaVersion"] = ArtifactContracts.ValidationResultSchemaVersion;
        }

        return root?.ToJsonString() ?? json;
    }

    private static void NormalizeUnmeasuredMetrics(JsonObject metrics, string denominator, params string[] values)
    {
        if (GetArtifactProperty(metrics, denominator) is JsonValue count && count.TryGetValue<double>(out var observed) && observed > 0) return;
        foreach (var value in values) metrics[FindArtifactPropertyName(metrics, value) ?? value] = null;
    }

    private static JsonNode? GetArtifactProperty(JsonObject value, string name) =>
        FindArtifactPropertyName(value, name) is { } key ? value[key] : null;

    private static string? FindArtifactPropertyName(JsonObject value, string name)
    {
        var matches = value.Select(property => property.Key)
            .Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1) throw new CliUsageException("Validation artifact contains ambiguous metadata aliases.");
        return matches.SingleOrDefault();
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
        if (file.Length == 0) throw new CliUsageException("Input files must have a non-empty declared length.");
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
        if (file.Length == 0) throw new CliUsageException("Input files must have a non-empty declared length.");
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