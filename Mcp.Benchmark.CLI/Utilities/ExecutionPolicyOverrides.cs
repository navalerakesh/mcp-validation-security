using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

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
        string[]? allowedOrigins = null)
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

            if (allowedOrigins is { Length: > 0 })
            {
                configuration.Execution.AllowedOrigins = allowedOrigins
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
            configuration.Execution.MaxRequests = maxRequests.Value;
        }

        if (maxConcurrency.HasValue)
        {
            configuration.Execution.MaxConcurrency = maxConcurrency.Value;
        }

        if (timeoutSeconds.HasValue)
        {
            configuration.Execution.TimeoutSeconds = timeoutSeconds.Value;
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

        if (configuration.Execution.TimeoutSeconds is >= ExecutionPolicyDefaults.MinimumPositiveValue and <= ExecutionPolicyDefaults.MaximumTimeoutSeconds)
        {
            configuration.Server.TimeoutMs = configuration.Execution.TimeoutSeconds * 1000;
        }

        if (configuration.Execution.Mode == ExecutionMode.Safe)
        {
            ApplySafeMode(configuration.Validation.Categories);
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