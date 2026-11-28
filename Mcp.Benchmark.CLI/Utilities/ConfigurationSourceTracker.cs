using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal sealed class ConfigurationSourceTracker
{
    private readonly Dictionary<string, ConfigurationSettingOrigin> _origins = new(StringComparer.Ordinal);

    internal void RecordFileSettings(IEnumerable<string> settings)
    {
        foreach (var setting in settings)
            Record(setting, ConfigurationSourceKind.File, "--config", "Explicit configuration file setting.");
    }

    internal void RecordCommandLine<T>(Expression<Func<McpValidatorConfiguration, T>> setting, string option) =>
        Record(setting, ConfigurationSourceKind.CommandLine, option, "Explicit CLI setting.");

    internal void Record<T>(Expression<Func<McpValidatorConfiguration, T>> setting,
        ConfigurationSourceKind source, string pointer, string reason) =>
        Record(GetSettingPath(setting), source, pointer, reason);

    internal ConfigurationSourceKind GetSource<T>(Expression<Func<McpValidatorConfiguration, T>> setting) =>
        _origins.GetValueOrDefault(GetSettingPath(setting))?.Source ?? ConfigurationSourceKind.Default;

    private static string GetSettingPath<T>(Expression<Func<McpValidatorConfiguration, T>> setting)
    {
        var segments = new Stack<string>();
        Expression current = setting.Body;
        while (current is MemberExpression { Member: PropertyInfo property } member)
        {
            segments.Push(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name);
            current = member.Expression!;
        }
        if (current != setting.Parameters[0] || segments.Count == 0)
            throw new ArgumentException("Configuration origins require a direct property path.", nameof(setting));
        return "$." + string.Join(".", segments);
    }

    internal void RecordBooleanChanges(McpValidatorConfiguration configuration, ConfigurationSourceKind source,
        string pointer, string reason, Action apply)
    {
        var before = BoundedArtifactReader.GetEffectiveBooleanSettings(configuration);
        apply();
        foreach (var setting in BoundedArtifactReader.GetEffectiveBooleanSettings(configuration))
        {
            if (!before.TryGetValue(setting.Key, out var previous) || previous != setting.Value)
                Record(setting.Key, source, pointer, reason);
        }
    }

    internal IReadOnlyList<ConfigurationSettingOrigin> Snapshot(McpValidatorConfiguration configuration) =>
        BoundedArtifactReader.GetEffectiveConfigurationSettingPaths(configuration)
            .Select(setting => _origins.GetValueOrDefault(setting) ?? new ConfigurationSettingOrigin(
                setting, ConfigurationSourceKind.Default, "built-in", null, "Default configuration value."))
            .ToArray();

    internal IReadOnlyList<ConfigurationSettingOrigin> Snapshot<T>(McpValidatorConfiguration configuration,
        Expression<Func<McpValidatorConfiguration, T>> section)
    {
        var prefix = GetSettingPath(section);
        return Snapshot(configuration).Where(origin => origin.Setting == prefix ||
            origin.Setting.StartsWith(prefix + ".", StringComparison.Ordinal)).ToArray();
    }

    private void Record(string setting, ConfigurationSourceKind source, string pointer, string reason)
    {
        var previous = _origins.TryGetValue(setting, out var origin) ? origin.Source : ConfigurationSourceKind.Default;
        _origins[setting] = new ConfigurationSettingOrigin(setting, source, pointer, previous, reason);
    }
}