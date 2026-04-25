using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class ExecutionPolicyOverrides
{
    public static void Apply(
        McpValidatorConfiguration configuration,
        int? maxConcurrency = null,
        string? executionMode = null,
        bool? dryRun = null,
        string[]? allowedHosts = null,
        bool? allowPrivateAddresses = null,
        int? maxRequests = null,
        int? timeoutSeconds = null,
        string? persistenceMode = null,
        string? redactLevel = null,
        string? traceMode = null,
        bool? confirmElevatedRisk = null,
        bool? enableModelEval = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Execution ??= new ExecutionPolicy();
        configuration.Evaluation ??= new EvaluationPolicy();
        configuration.Evaluation.ModelEvaluation ??= new ModelEvaluationPolicy();

        if (TryParseEnum(executionMode, out ExecutionMode parsedExecutionMode))
        {
            configuration.Execution.Mode = parsedExecutionMode;
        }

        if (dryRun.HasValue)
        {
            configuration.Execution.DryRun = dryRun.Value;
        }

        if (allowedHosts is { Length: > 0 })
        {
            configuration.Execution.AllowedHosts = allowedHosts
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (allowPrivateAddresses.HasValue)
        {
            configuration.Execution.AllowPrivateAddresses = allowPrivateAddresses.Value;
        }

        if (maxRequests.HasValue)
        {
            configuration.Execution.MaxRequests = Math.Max(1, maxRequests.Value);
        }

        if (maxConcurrency.HasValue)
        {
            configuration.Execution.MaxConcurrency = Math.Clamp(maxConcurrency.Value, 1, 128);
        }

        if (timeoutSeconds.HasValue)
        {
            configuration.Execution.TimeoutSeconds = Math.Max(1, timeoutSeconds.Value);
        }

        if (TryParseEnum(persistenceMode?.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase), out PersistenceMode parsedPersistenceMode))
        {
            configuration.Execution.PersistenceMode = parsedPersistenceMode;
        }

        if (TryParseEnum(redactLevel, out RedactionLevel parsedRedactionLevel))
        {
            configuration.Execution.RedactLevel = parsedRedactionLevel;
        }

        if (TryParseEnum(traceMode, out TraceMode parsedTraceMode))
        {
            configuration.Execution.TraceMode = parsedTraceMode;
        }

        if (confirmElevatedRisk.HasValue)
        {
            configuration.Execution.ConfirmElevatedRisk = confirmElevatedRisk.Value;
        }

        if (enableModelEval.HasValue)
        {
            configuration.Evaluation.ModelEvaluation.Enabled = enableModelEval.Value;
        }

        configuration.Execution.MaxConcurrency = Math.Clamp(configuration.Execution.MaxConcurrency, 1, 128);
        configuration.Execution.MaxRequests = Math.Max(1, configuration.Execution.MaxRequests);
        configuration.Execution.TimeoutSeconds = Math.Max(1, configuration.Execution.TimeoutSeconds);
        configuration.Server.TimeoutMs = configuration.Execution.TimeoutSeconds * 1000;
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out parsed))
        {
            return true;
        }

        parsed = default;
        return false;
    }
}