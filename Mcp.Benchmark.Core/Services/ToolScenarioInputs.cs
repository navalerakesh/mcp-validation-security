using System.Collections.ObjectModel;
using System.Buffers;
using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ToolScenarioInputs
{
    public const int MaximumScenarios = 64;
    public const int MaximumArgumentBytes = 64 * 1024;
    public const int MaximumAggregateArgumentBytes = 1024 * 1024;
    public const int MaximumArgumentValues = 64;
    public const int MaximumArgumentDepth = 16;
    public const int MaximumToolNameCharacters = 4096;

    public static IReadOnlyDictionary<string, JsonElement> CapturePositiveScenarios(ToolTestingConfig configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.CustomScenarios is null || configuration.CustomScenarios.Count > MaximumScenarios)
            throw Invalid("Tool scenario inventory exceeds its admitted bounds.");
        if (configuration.CustomScenarios.Count > 0 && configuration.SpecificTools is not { Count: 0 })
            throw Invalid("Custom tool arguments cannot be combined with a separate tool selection in this build.");
        var captured = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var bytes = 0;
        foreach (var scenario in configuration.CustomScenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.ToolName) || scenario.ToolName.Length > MaximumToolNameCharacters ||
                scenario.ExpectedResult is not null || scenario.ExpectError || scenario.Parameters is null || captured.ContainsKey(scenario.ToolName))
                throw Invalid("Tool scenarios require one unique exact name and available positive arguments; result patterns and expected errors are unsupported.");
            var values = MaximumArgumentValues;
            ValidateValue(scenario.Parameters, 0, ref values, inspectJsonElements: false);
            var buffer = new ArgumentBuffer(Math.Min(MaximumArgumentBytes, MaximumAggregateArgumentBytes - bytes));
            try
            {
                using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaximumArgumentDepth + 1 });
                JsonSerializer.Serialize(writer, scenario.Parameters, new JsonSerializerOptions { MaxDepth = MaximumArgumentDepth + 1 });
                writer.Flush();
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
            { throw Invalid("Tool scenario arguments must be bounded JSON values."); }
            bytes += buffer.Written.Length;
            using var document = JsonDocument.Parse(buffer.Written);
            values = MaximumArgumentValues;
            ValidateValue(document.RootElement, 0, ref values, inspectJsonElements: true);
            captured.Add(scenario.ToolName, document.RootElement.Clone());
        }
        return new ReadOnlyDictionary<string, JsonElement>(captured);
    }

    private static void ValidateValue(object? value, int depth, ref int remaining, bool inspectJsonElements)
    {
        if (depth > MaximumArgumentDepth || --remaining < 0) throw Invalid("Tool scenario arguments exceed their admitted structural bounds.");
        switch (value)
        {
            case null or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                return;
            case double number when double.IsFinite(number): return;
            case float number when float.IsFinite(number): return;
            case string text when text.Length <= MaximumArgumentBytes: return;
            case JsonElement element:
                if (!inspectJsonElements) return;
                switch (element.ValueKind)
                {
                    case JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False: return;
                    case JsonValueKind.String when element.GetString()!.Length <= MaximumArgumentBytes: return;
                    case JsonValueKind.Number when element.GetRawText().Length <= MaximumArgumentBytes: return;
                    case JsonValueKind.Array:
                        foreach (var item in element.EnumerateArray()) ValidateValue(item, depth + 1, ref remaining, inspectJsonElements);
                        return;
                    case JsonValueKind.Object:
                        var names = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var property in element.EnumerateObject())
                        {
                            if (property.Name.Length > MaximumArgumentBytes || !names.Add(property.Name)) throw Invalid("Tool scenario argument object is invalid.");
                            ValidateValue(property.Value, depth + 1, ref remaining, inspectJsonElements);
                        }
                        return;
                }
                break;
            case Dictionary<string, object> dictionary:
                foreach (var entry in dictionary)
                {
                    if (entry.Key.Length > MaximumArgumentBytes) throw Invalid("Tool scenario argument name exceeds its admitted bounds.");
                    ValidateValue(entry.Value, depth + 1, ref remaining, inspectJsonElements);
                }
                return;
            case Array array when array.Rank == 1:
                foreach (var item in array) ValidateValue(item, depth + 1, ref remaining, inspectJsonElements);
                return;
            case List<object> list:
                foreach (var item in list) ValidateValue(item, depth + 1, ref remaining, inspectJsonElements);
                return;
        }
        throw Invalid("Tool scenario arguments must contain only bounded JSON values.");
    }

    private static ArgumentException Invalid(string message) => new(message);

    private sealed class ArgumentBuffer(int limit) : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public ReadOnlyMemory<byte> Written => _buffer.WrittenMemory;

        public void Advance(int count)
        {
            if (count < 0 || count > limit - _buffer.WrittenCount) throw Invalid("Tool scenario arguments exceed their admitted byte bounds.");
            _buffer.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            var maximumReservation = 6 * limit - _buffer.WrittenCount;
            if (sizeHint < 0 || Math.Max(sizeHint, 1) > maximumReservation) throw Invalid("Tool scenario arguments exceed their admitted byte bounds.");
            var memory = _buffer.GetMemory(sizeHint);
            return memory[..Math.Min(memory.Length, maximumReservation)];
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
    }
}