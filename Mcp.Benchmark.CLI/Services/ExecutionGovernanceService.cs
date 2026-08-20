using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Constants;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.CLI.Services;

public sealed class ExecutionGovernanceService : IExecutionGovernanceService
{
    public async Task<IReadOnlyList<string>> ValidateTargetResolutionAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.AllowPrivateAddresses ||
            !Uri.TryCreate(plan.Target, UriKind.Absolute, out var targetUri) ||
            targetUri.Scheme is not ("http" or "https"))
        {
            return Array.Empty<string>();
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(targetUri.DnsSafeHost, cancellationToken);
            if (addresses.Length == 0)
            {
                return [$"Target host '{targetUri.Host}' did not resolve to an address."];
            }

            if (addresses.Any(NetworkAddressClassifier.IsRestricted))
            {
                return [$"Target host '{targetUri.Host}' resolves to a private, local, or reserved address. Re-run with --allow-private-addresses only in an isolated environment after review."];
            }

            return Array.Empty<string>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return [$"Target host '{targetUri.Host}' could not be resolved safely: {ex.Message}"];
        }
    }

    public ExecutionPlan BuildValidationPlan(CliSessionContext sessionContext, McpValidatorConfiguration configuration, string? outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(configuration);

        var executionPolicy = configuration.Execution ??= new ExecutionPolicy();
        var validation = configuration.Validation ??= new ValidationConfig();
        var categories = validation.Categories ??= new ValidationScenarios();

        return BuildPlan(
            sessionContext,
            commandName: "validate",
            configuration.Server,
            executionPolicy,
            configuration.Reporting?.SpecProfile ?? categories.ProtocolCompliance?.ProtocolVersion,
            outputDirectory,
            configuration.Evaluation?.ModelEvaluation?.Enabled == true,
            configuration.ClientProfiles?.Profiles?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>(),
            CollectPlannedChecks(categories),
            CollectPlannedArtifacts(configuration, outputDirectory),
            ArtifactDigestUtility.ComputeObject(configuration.CloneWithoutSecrets()));
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
            serverConfig.ProtocolVersion,
            outputDirectory,
            modelEvaluationEnabled: false,
            selectedClientProfiles: Array.Empty<string>(),
            plannedChecks,
            plannedArtifacts,
            ArtifactDigestUtility.ComputeObject(serverConfig.CloneWithoutSecrets()));
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
            RequestedProtocolProfile = plan.RequestedProtocolProfile,
            ResolvedSchemaVersion = plan.ResolvedSchemaVersion,
            ProtocolEra = plan.ProtocolEra,
            ProtocolEraSelection = plan.ProtocolEraSelection,
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
            MaxResponseBytes = plan.MaxResponseBytes,
            AllowedHosts = plan.AllowedHosts,
            AllowedOrigins = plan.AllowedOrigins,
            PlannedChecks = plan.PlannedChecks,
            ExecutedChecks = result == null ? Array.Empty<string>() : CollectExecutedChecks(result),
            ArtifactPaths = artifactPaths.Select(Path.GetFileName).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToArray(),
            Digests = BuildDigests(plan, result, artifactPaths),
            EvidenceCompleteness = result?.VerdictAssessment?.EvidenceSummary,
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
        string? requestedProtocolProfile,
        string? outputDirectory,
        bool modelEvaluationEnabled,
        IReadOnlyList<string> selectedClientProfiles,
        IReadOnlyList<string> plannedChecks,
        IReadOnlyList<string> plannedArtifacts,
        string configDigest)
    {
        var allowedHosts = ResolveAllowedHosts(executionPolicy, serverConfig);
        IReadOnlyList<string> allowedOrigins;
        var originErrors = new List<string>();
        try
        {
            allowedOrigins = ResolveAllowedOrigins(executionPolicy, serverConfig, allowedHosts);
        }
        catch (ArgumentException ex)
        {
            allowedOrigins = Array.Empty<string>();
            originErrors.Add($"Execution allowedOrigins is invalid: {ex.Message}");
        }
        var validationErrors = Validate(serverConfig, executionPolicy, outputDirectory, allowedHosts, allowedOrigins, modelEvaluationEnabled).ToList();
        validationErrors.AddRange(originErrors);
        var requestedProfile = string.IsNullOrWhiteSpace(requestedProtocolProfile) ? "latest" : requestedProtocolProfile.Trim();
        var resolvedSchemaVersion = ResolveRequestedProtocolVersion(requestedProfile, serverConfig.ProtocolEra);
        var protocolEra = ProtocolEraVersions.IsModern(resolvedSchemaVersion)
            ? McpProtocolEra.Modern
            : McpProtocolEra.Legacy;
        if (serverConfig.ProtocolEra == McpProtocolEraSelection.Modern && protocolEra != McpProtocolEra.Modern)
        {
            validationErrors.Add($"Protocol era 'modern' conflicts with requested profile '{requestedProfile}'.");
        }
        else if (serverConfig.ProtocolEra == McpProtocolEraSelection.Legacy && protocolEra != McpProtocolEra.Legacy)
        {
            validationErrors.Add($"Protocol era 'legacy' conflicts with requested profile '{requestedProfile}'.");
        }

        var safeTarget = serverConfig.CloneWithoutSecrets().Endpoint ?? string.Empty;
        return new ExecutionPlan
        {
            ValidatorDigest = GetValidatorDigest(),
            ConfigDigest = configDigest,
            TargetDigest = ArtifactDigestUtility.ComputeText(safeTarget),
            CommandName = commandName,
            SessionId = sessionContext.SessionId,
            Target = safeTarget,
            Transport = serverConfig.Transport,
            RequestedProtocolProfile = requestedProfile,
            ResolvedSchemaVersion = resolvedSchemaVersion,
            ProtocolEra = protocolEra,
            ProtocolEraSelection = serverConfig.ProtocolEra,
            ExecutionMode = executionPolicy.Mode,
            DryRun = executionPolicy.DryRun,
            PersistenceMode = executionPolicy.PersistenceMode,
            RedactionLevel = executionPolicy.RedactLevel,
            TraceMode = executionPolicy.TraceMode,
            MaxRequests = Math.Clamp(
                executionPolicy.MaxRequests,
                ExecutionPolicyDefaults.MinimumPositiveValue,
                ExecutionPolicyDefaults.MaximumRequests),
            MaxConcurrency = Math.Clamp(
                executionPolicy.MaxConcurrency,
                ExecutionPolicyDefaults.MinimumPositiveValue,
                ExecutionPolicyDefaults.MaximumConcurrency),
            TimeoutSeconds = Math.Max(ExecutionPolicyDefaults.MinimumPositiveValue, executionPolicy.TimeoutSeconds),
            MaxResponseBytes = Math.Clamp(
                executionPolicy.MaxResponseBytes,
                ExecutionPolicyDefaults.MinimumPositiveValue,
                ExecutionPolicyDefaults.MaximumResponseBytes),
            AllowPrivateAddresses = executionPolicy.AllowPrivateAddresses,
            RequiresElevatedRiskAcknowledgement = executionPolicy.Mode == ExecutionMode.Elevated,
            ElevatedRiskAcknowledged = executionPolicy.ConfirmElevatedRisk,
            OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory,
            SessionArtifactsEnabled = executionPolicy.PersistenceMode == PersistenceMode.Session,
            SessionLogsEnabled = executionPolicy.PersistenceMode == PersistenceMode.Session,
            ModelEvaluationEnabled = modelEvaluationEnabled,
            AllowedHosts = allowedHosts,
            AllowedOrigins = allowedOrigins,
            SelectedClientProfiles = selectedClientProfiles,
            PlannedChecks = plannedChecks,
            PlannedArtifacts = plannedArtifacts,
            ValidationErrors = validationErrors
        };
    }

    private static AuditDigestSet BuildDigests(
        ExecutionPlan plan,
        ValidationResult? result,
        IEnumerable<string> artifactPaths)
    {
        var ruleCatalog = result?.VerdictAssessment?.Policy.RuleRevisions
            .Select(pair => $"{pair.Key}={pair.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            ?? Enumerable.Empty<string>();
        var profileCatalog = (result?.Evidence.AppliedPacks
            .Where(pack => pack.Key.Value.Contains("client-profile", StringComparison.OrdinalIgnoreCase))
            .Select(pack => $"{pack.Key.Value}={pack.Revision.Value}")
            ?? Enumerable.Empty<string>())
            .Concat(result?.ClientCompatibility?.RequestedProfiles.Select(profile => $"profile={profile}")
                ?? Enumerable.Empty<string>())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var artifactDigests = artifactPaths
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetFileName(path),
                ArtifactDigestUtility.ComputeFile,
                StringComparer.Ordinal);

        return new AuditDigestSet
        {
            ValidatorSha256 = plan.ValidatorDigest,
            RuleCatalogSha256 = ArtifactDigestUtility.ComputeText(string.Join("\n", ruleCatalog)),
            ProfileCatalogSha256 = ArtifactDigestUtility.ComputeText(string.Join("\n", profileCatalog)),
            ConfigSha256 = plan.ConfigDigest,
            TargetIdentitySha256 = plan.TargetDigest,
            ArtifactsSha256 = new SortedDictionary<string, string>(artifactDigests, StringComparer.Ordinal)
        };
    }

    private static string GetValidatorDigest()
    {
        var assembly = typeof(ExecutionGovernanceService).Assembly;
        return string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)
            ? ArtifactDigestUtility.ComputeText(
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
            : ArtifactDigestUtility.ComputeFile(assembly.Location);
    }

    private static string ResolveRequestedProtocolVersion(string requestedProfile, McpProtocolEraSelection selection)
    {
        if (selection == McpProtocolEraSelection.Legacy &&
            string.Equals(requestedProfile, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolEraVersions.GetLatestLegacyVersion().Value;
        }

        return SchemaRegistryProtocolVersions.NormalizeRequestedVersion(requestedProfile);
    }

    private static IReadOnlyList<string> Validate(
        McpServerConfig serverConfig,
        ExecutionPolicy execution,
        string? outputDirectory,
        IReadOnlyList<string> allowedHosts,
        IReadOnlyList<string> allowedOrigins,
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
        else if (execution.MaxRequests > ExecutionPolicyDefaults.MaximumRequests)
        {
            errors.Add($"Execution maxRequests must not exceed {ExecutionPolicyDefaults.MaximumRequests}.");
        }

        if (execution.MaxConcurrency < 1)
        {
            errors.Add("Execution maxConcurrency must be at least 1.");
        }
        else if (execution.MaxConcurrency > ExecutionPolicyDefaults.MaximumConcurrency)
        {
            errors.Add($"Execution maxConcurrency must not exceed {ExecutionPolicyDefaults.MaximumConcurrency}.");
        }

        if (execution.TimeoutSeconds < 1)
        {
            errors.Add("Execution timeoutSeconds must be at least 1.");
        }
        else if (execution.TimeoutSeconds > ExecutionPolicyDefaults.MaximumTimeoutSeconds)
        {
            errors.Add($"Execution timeoutSeconds must not exceed {ExecutionPolicyDefaults.MaximumTimeoutSeconds}.");
        }

        if (execution.MaxResponseBytes < 1 || execution.MaxResponseBytes > ExecutionPolicyDefaults.MaximumResponseBytes)
        {
            errors.Add($"Execution maxResponseBytes must be between 1 and {ExecutionPolicyDefaults.MaximumResponseBytes}.");
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
            if (IPAddress.TryParse(endpointUri.Host, out var endpointAddress) &&
                NetworkAddressClassifier.IsRestricted(endpointAddress) &&
                !execution.AllowPrivateAddresses)
            {
                errors.Add($"Target host '{endpointUri.Host}' is private or loopback. Re-run with --allow-private-addresses to opt in.");
            }

            var endpointHost = NetworkTargetPolicy.NormalizeHost(endpointUri.IdnHost);
            if (allowedHosts.Count > 0 && !allowedHosts.Contains(endpointHost, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Target host '{endpointHost}' is not in the allowed host set.");
            }

            var endpointOrigin = NetworkTargetPolicy.NormalizeOrigin(endpointUri);
            if (allowedOrigins.Count > 0 && !allowedOrigins.Contains(endpointOrigin, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Target origin '{endpointOrigin}' is not in the allowed origin set.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ResolveAllowedHosts(ExecutionPolicy execution, McpServerConfig server)
    {
        var configured = execution.AllowedHosts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NetworkTargetPolicy.NormalizeHost)
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
            configured.Add(NetworkTargetPolicy.NormalizeHost(endpointUri.IdnHost));
        }

        return configured;
    }

    private static IReadOnlyList<string> ResolveAllowedOrigins(
        ExecutionPolicy execution,
        McpServerConfig server,
        IReadOnlyList<string> allowedHosts)
    {
        var configured = execution.AllowedOrigins
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NetworkTargetPolicy.NormalizeOrigin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configured.Count > 0)
        {
            return configured;
        }

        Uri? targetUri = null;
        if (!string.IsNullOrWhiteSpace(server.Endpoint) &&
            Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var parsedTarget) &&
            parsedTarget.Scheme is "http" or "https")
        {
            targetUri = parsedTarget;
            configured.Add(NetworkTargetPolicy.NormalizeOrigin(parsedTarget));
        }

        foreach (var host in allowedHosts.Where(host => !string.Equals(host, targetUri?.IdnHost, StringComparison.OrdinalIgnoreCase)))
        {
            configured.Add(NetworkTargetPolicy.CreateHttpsOrigin(host));
        }

        return configured.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
            artifacts.Add("junit-report");
        }

        if (configuration.ClientProfiles?.Profiles.Count > 0)
        {
            artifacts.Add("client-profile-summary");
        }

        if (configuration.Evaluation?.ModelEvaluation?.Enabled == true)
        {
            artifacts.Add("model-evaluation");
        }

        if (configuration.Reporting.SignAttestation)
        {
            artifacts.Add("validation-attestation");
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

}