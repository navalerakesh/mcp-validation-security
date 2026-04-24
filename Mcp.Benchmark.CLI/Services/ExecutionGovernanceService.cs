using System.Net;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services;

public sealed class ExecutionGovernanceService : IExecutionGovernanceService
{
    public ExecutionPlan BuildValidationPlan(CliSessionContext sessionContext, McpValidatorConfiguration configuration, string? outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Execution ??= new ExecutionPolicy();
        configuration.Validation ??= new ValidationConfig();
        configuration.Validation.Categories ??= new ValidationScenarios();

        return BuildPlan(
            sessionContext,
            commandName: "validate",
            configuration.Server,
            configuration.Execution,
            outputDirectory,
            configuration.Evaluation?.ModelEvaluation?.Enabled == true,
            configuration.ClientProfiles?.Profiles?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>(),
            CollectPlannedChecks(configuration.Validation.Categories),
            CollectPlannedArtifacts(configuration, outputDirectory));
    }

    public ExecutionPlan BuildCommandPlan(
        CliSessionContext sessionContext,
        string commandName,
        McpServerConfig serverConfig,
        ExecutionPolicy? executionPolicy,
        string? outputDirectory,
        IReadOnlyList<string> plannedChecks,
        IReadOnlyList<string> plannedArtifacts)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        return BuildPlan(
            sessionContext,
            commandName,
            serverConfig,
            executionPolicy ?? new ExecutionPolicy(),
            outputDirectory,
            modelEvaluationEnabled: false,
            selectedClientProfiles: Array.Empty<string>(),
            plannedChecks,
            plannedArtifacts);
    }

    public AuditManifest BuildAuditManifest(
        ExecutionPlan plan,
        ValidationResult? result,
        IReadOnlyList<string> artifactPaths,
        ModelEvaluationArtifact? modelEvaluationArtifact)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new AuditManifest
        {
            CommandName = plan.CommandName,
            ValidationId = result?.ValidationId ?? plan.SessionId,
            SessionId = plan.SessionId,
            Target = plan.Target,
            Transport = plan.Transport,
            ExecutionMode = plan.ExecutionMode,
            DryRun = plan.DryRun,
            PersistenceMode = plan.PersistenceMode,
            RedactionLevel = plan.RedactionLevel,
            TraceMode = plan.TraceMode,
            ModelEvaluationEnabled = plan.ModelEvaluationEnabled,
            ModelEvaluationStatus = modelEvaluationArtifact?.Status.ToString(),
            AllowPrivateAddresses = plan.AllowPrivateAddresses,
            MaxRequests = plan.MaxRequests,
            MaxConcurrency = plan.MaxConcurrency,
            TimeoutSeconds = plan.TimeoutSeconds,
            AllowedHosts = plan.AllowedHosts,
            PlannedChecks = plan.PlannedChecks,
            ExecutedChecks = result == null ? Array.Empty<string>() : CollectExecutedChecks(result),
            ArtifactPaths = artifactPaths,
            OverallStatus = result?.OverallStatus,
            BaselineVerdict = result?.VerdictAssessment?.BaselineVerdict,
            ProtocolVerdict = result?.VerdictAssessment?.ProtocolVerdict,
            CoverageVerdict = result?.VerdictAssessment?.CoverageVerdict
        };
    }

    private static ExecutionPlan BuildPlan(
        CliSessionContext sessionContext,
        string commandName,
        McpServerConfig serverConfig,
        ExecutionPolicy executionPolicy,
        string? outputDirectory,
        bool modelEvaluationEnabled,
        IReadOnlyList<string> selectedClientProfiles,
        IReadOnlyList<string> plannedChecks,
        IReadOnlyList<string> plannedArtifacts)
    {
        var allowedHosts = ResolveAllowedHosts(executionPolicy, serverConfig);
        var validationErrors = Validate(serverConfig, executionPolicy, outputDirectory, allowedHosts, modelEvaluationEnabled);

        return new ExecutionPlan
        {
            CommandName = commandName,
            SessionId = sessionContext.SessionId,
            Target = serverConfig.Endpoint ?? string.Empty,
            Transport = serverConfig.Transport,
            ExecutionMode = executionPolicy.Mode,
            DryRun = executionPolicy.DryRun,
            PersistenceMode = executionPolicy.PersistenceMode,
            RedactionLevel = executionPolicy.RedactLevel,
            TraceMode = executionPolicy.TraceMode,
            MaxRequests = Math.Max(1, executionPolicy.MaxRequests),
            MaxConcurrency = Math.Max(1, executionPolicy.MaxConcurrency),
            TimeoutSeconds = Math.Max(1, executionPolicy.TimeoutSeconds),
            AllowPrivateAddresses = executionPolicy.AllowPrivateAddresses,
            RequiresElevatedRiskAcknowledgement = executionPolicy.Mode == ExecutionMode.Elevated,
            ElevatedRiskAcknowledged = executionPolicy.ConfirmElevatedRisk,
            OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory,
            SessionArtifactsEnabled = executionPolicy.PersistenceMode == PersistenceMode.Session,
            SessionLogsEnabled = executionPolicy.PersistenceMode == PersistenceMode.Session,
            ModelEvaluationEnabled = modelEvaluationEnabled,
            AllowedHosts = allowedHosts,
            SelectedClientProfiles = selectedClientProfiles,
            PlannedChecks = plannedChecks,
            PlannedArtifacts = plannedArtifacts,
            ValidationErrors = validationErrors
        };
    }

    private static IReadOnlyList<string> Validate(
        McpServerConfig serverConfig,
        ExecutionPolicy execution,
        string? outputDirectory,
        IReadOnlyList<string> allowedHosts,
        bool modelEvaluationEnabled)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            errors.Add("A server endpoint or STDIO command is required.");
        }

        if (execution.Mode == ExecutionMode.Elevated && !execution.ConfirmElevatedRisk)
        {
            errors.Add("Elevated execution requires --confirm-elevated-risk before contacting the target.");
        }

        if (execution.MaxRequests < 1)
        {
            errors.Add("Execution maxRequests must be at least 1.");
        }

        if (execution.MaxConcurrency < 1)
        {
            errors.Add("Execution maxConcurrency must be at least 1.");
        }

        if (execution.TimeoutSeconds < 1)
        {
            errors.Add("Execution timeoutSeconds must be at least 1.");
        }

        if (execution.PersistenceMode == PersistenceMode.ExplicitOutput && string.IsNullOrWhiteSpace(outputDirectory))
        {
            errors.Add("Persistence mode 'explicit-output' requires an output directory for operational artifacts.");
        }

        if (modelEvaluationEnabled &&
            execution.PersistenceMode != PersistenceMode.Session &&
            string.IsNullOrWhiteSpace(outputDirectory))
        {
            errors.Add("Model evaluation requires --output or persistence-mode session so advisory artifacts remain separate from canonical results.");
        }

        if (!string.IsNullOrWhiteSpace(serverConfig.Endpoint) &&
            Uri.TryCreate(serverConfig.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            if (IsPrivateAddress(endpointUri.Host) && !execution.AllowPrivateAddresses)
            {
                errors.Add($"Target host '{endpointUri.Host}' is private or loopback. Re-run with --allow-private-addresses to opt in.");
            }

            if (allowedHosts.Count > 0 && !allowedHosts.Contains(endpointUri.Host, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Target host '{endpointUri.Host}' is not in the allowed host set.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ResolveAllowedHosts(ExecutionPolicy execution, McpServerConfig server)
    {
        var configured = execution.AllowedHosts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configured.Count > 0)
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(server.Endpoint) &&
            Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var endpointUri) &&
            !string.IsNullOrWhiteSpace(endpointUri.Host))
        {
            configured.Add(endpointUri.Host);
        }

        return configured;
    }

    private static IReadOnlyList<string> CollectPlannedChecks(ValidationScenarios categories)
    {
        var checks = new List<string>();

        if (categories.ProtocolCompliance.TestJsonRpcCompliance)
        {
            checks.Add("protocol-compliance");
        }

        if (categories.ToolTesting.TestToolDiscovery || categories.ToolTesting.TestToolExecution || categories.ToolTesting.TestParameterValidation)
        {
            checks.Add("tool-validation");
        }

        if (categories.ResourceTesting.TestResourceDiscovery || categories.ResourceTesting.TestResourceReading || categories.ResourceTesting.TestUriValidation)
        {
            checks.Add("resource-validation");
        }

        if (categories.PromptTesting.TestPromptDiscovery || categories.PromptTesting.TestPromptExecution || categories.PromptTesting.TestArgumentValidation)
        {
            checks.Add("prompt-validation");
        }

        if (categories.SecurityTesting.TestInputValidation || categories.SecurityTesting.TestInjectionAttacks || categories.SecurityTesting.TestAuthenticationBypass)
        {
            checks.Add("security-testing");
        }

        if (categories.PerformanceTesting.TestConcurrentRequests || categories.PerformanceTesting.TestResponseTimes || categories.PerformanceTesting.TestThroughput)
        {
            checks.Add("performance-testing");
        }

        if (categories.ErrorHandling.TestInvalidMethods || categories.ErrorHandling.TestMalformedJson || categories.ErrorHandling.TestTimeoutHandling)
        {
            checks.Add("error-handling");
        }

        return checks;
    }

    private static IReadOnlyList<string> CollectPlannedArtifacts(McpValidatorConfiguration configuration, string? outputDirectory)
    {
        var artifacts = new List<string> { "audit-manifest" };

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            artifacts.Add("markdown-report");
            artifacts.Add("html-report");
            artifacts.Add("result-json");
            artifacts.Add("sarif-report");
        }

        if (configuration.ClientProfiles?.Profiles.Count > 0)
        {
            artifacts.Add("client-profile-summary");
        }

        if (configuration.Evaluation?.ModelEvaluation?.Enabled == true)
        {
            artifacts.Add("model-evaluation");
        }

        if (configuration.Execution?.PersistenceMode == PersistenceMode.Session)
        {
            artifacts.Add("session-artifacts");
            artifacts.Add("session-log");
        }

        return artifacts;
    }

    private static IReadOnlyList<string> CollectExecutedChecks(ValidationResult result)
    {
        var executed = new List<string>();

        if (result.ProtocolCompliance != null)
        {
            executed.Add("protocol-compliance");
        }

        if (result.ToolValidation != null)
        {
            executed.Add("tool-validation");
        }

        if (result.ResourceTesting != null)
        {
            executed.Add("resource-validation");
        }

        if (result.PromptTesting != null)
        {
            executed.Add("prompt-validation");
        }

        if (result.SecurityTesting != null)
        {
            executed.Add("security-testing");
        }

        if (result.PerformanceTesting != null)
        {
            executed.Add("performance-testing");
        }

        if (result.ErrorHandling != null)
        {
            executed.Add("error-handling");
        }

        return executed;
    }

    private static bool IsPrivateAddress(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
    }
}