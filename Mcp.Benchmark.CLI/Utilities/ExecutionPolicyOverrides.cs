using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.CLI.Models;

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
        bool? enableModelEval = null,
        string[]? allowedOrigins = null,
        ConfigurationSourceTracker? sources = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Execution ??= new ExecutionPolicy();
        configuration.Evaluation ??= new EvaluationPolicy();
        configuration.Evaluation.ModelEvaluation ??= new ModelEvaluationPolicy();

        if (TryParseEnum(executionMode, out ExecutionMode parsedExecutionMode))
        {
            configuration.Execution.Mode = parsedExecutionMode;
            sources?.RecordCommandLine(value => value.Execution!.Mode, "--mode");
        }

        if (dryRun.HasValue)
        {
            configuration.Execution.DryRun = dryRun.Value;
            sources?.RecordCommandLine(value => value.Execution!.DryRun, "--dry-run");
        }

        if (allowedHosts is { Length: > 0 })
        {
            configuration.Execution.AllowedHosts = allowedHosts
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            sources?.RecordCommandLine(value => value.Execution!.AllowedHosts, "--allow-host");
        }

            if (allowedOrigins is { Length: > 0 })
            {
                configuration.Execution.AllowedOrigins = allowedOrigins
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
                sources?.RecordCommandLine(value => value.Execution!.AllowedOrigins, "--allow-origin");
            }

        if (allowPrivateAddresses.HasValue)
        {
            configuration.Execution.AllowPrivateAddresses = allowPrivateAddresses.Value;
            sources?.RecordCommandLine(value => value.Execution!.AllowPrivateAddresses, "--allow-private-addresses");
        }

        if (maxRequests.HasValue)
        {
            configuration.Execution.MaxRequests = maxRequests.Value;
            sources?.RecordCommandLine(value => value.Execution!.MaxRequests, "--max-requests");
        }

        if (maxConcurrency.HasValue)
        {
            configuration.Execution.MaxConcurrency = maxConcurrency.Value;
            sources?.RecordCommandLine(value => value.Execution!.MaxConcurrency, "--max-concurrency");
        }

        if (timeoutSeconds.HasValue)
        {
            configuration.Execution.TimeoutSeconds = timeoutSeconds.Value;
            sources?.RecordCommandLine(value => value.Execution!.TimeoutSeconds, "--timeout");
        }

        if (TryParseEnum(persistenceMode?.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase), out PersistenceMode parsedPersistenceMode))
        {
            configuration.Execution.PersistenceMode = parsedPersistenceMode;
            sources?.RecordCommandLine(value => value.Execution!.PersistenceMode, "--persistence-mode");
        }

        if (TryParseEnum(redactLevel, out RedactionLevel parsedRedactionLevel))
        {
            configuration.Execution.RedactLevel = parsedRedactionLevel;
            sources?.RecordCommandLine(value => value.Execution!.RedactLevel, "--redact-level");
        }

        if (TryParseEnum(traceMode, out TraceMode parsedTraceMode))
        {
            configuration.Execution.TraceMode = parsedTraceMode;
            sources?.RecordCommandLine(value => value.Execution!.TraceMode, "--trace");
        }

        if (confirmElevatedRisk.HasValue)
        {
            configuration.Execution.ConfirmElevatedRisk = confirmElevatedRisk.Value;
            sources?.RecordCommandLine(value => value.Execution!.ConfirmElevatedRisk, "--confirm-elevated-risk");
        }

        if (enableModelEval.HasValue)
        {
            configuration.Evaluation.ModelEvaluation.Enabled = enableModelEval.Value;
            sources?.RecordCommandLine(value => value.Evaluation!.ModelEvaluation.Enabled, "--enable-model-eval");
        }

        if (configuration.Execution.TimeoutSeconds is >= ExecutionPolicyDefaults.MinimumPositiveValue and <= ExecutionPolicyDefaults.MaximumTimeoutSeconds)
        {
            configuration.Server.TimeoutMs = configuration.Execution.TimeoutSeconds * 1000;
            sources?.Record(value => value.Server.TimeoutMs, ConfigurationSourceKind.Governance,
                nameof(ExecutionPolicy.TimeoutSeconds), "Derived from the effective execution timeout budget.");
        }

        if (configuration.Execution.Mode == ExecutionMode.Safe)
        {
            if (sources is null)
                ApplySafeMode(configuration.Validation.Categories);
            else
                sources.RecordBooleanChanges(configuration, ConfigurationSourceKind.Governance, "safe-mode",
                    "Safe mode disallows active probes.", () => ApplySafeMode(configuration.Validation.Categories));
        }
    }

    private static void ApplySafeMode(ValidationScenarios scenarios)
    {
        scenarios.ToolTesting.TestToolExecution = false;
        scenarios.ToolTesting.TestParameterValidation = false;
        scenarios.ResourceTesting.TestResourceReading = false;
        scenarios.ResourceTesting.TestSubscriptions = false;
        scenarios.PromptTesting.TestPromptExecution = false;
        scenarios.PromptTesting.TestArgumentValidation = false;

        scenarios.SecurityTesting.TestInputValidation = false;
        scenarios.SecurityTesting.TestInjectionAttacks = false;
        scenarios.SecurityTesting.TestAuthenticationBypass = false;
        scenarios.SecurityTesting.TestBufferOverflow = false;
        scenarios.SecurityTesting.TestMalformedMessages = false;
        scenarios.SecurityTesting.TestResourceExhaustion = false;

        scenarios.PerformanceTesting.TestConcurrentRequests = false;
        scenarios.PerformanceTesting.TestResponseTimes = false;
        scenarios.PerformanceTesting.TestMemoryUsage = false;
        scenarios.PerformanceTesting.TestThroughput = false;

        scenarios.ErrorHandling.TestInvalidMethods = false;
        scenarios.ErrorHandling.TestMalformedJson = false;
        scenarios.ErrorHandling.TestConnectionInterruption = false;
        scenarios.ErrorHandling.TestTimeoutHandling = false;
        scenarios.ErrorHandling.TestGracefulDegradation = false;

        scenarios.ProtocolCompliance.TestNotifications = false;
        scenarios.ProtocolCompliance.TestMessageFormat = false;
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