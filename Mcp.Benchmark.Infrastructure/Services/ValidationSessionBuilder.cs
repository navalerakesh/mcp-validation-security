using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using CoreLogLevel = Mcp.Benchmark.Core.Models.LogLevel;

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Builds the validation session context by negotiating protocol versions, capturing capability snapshots,
/// and performing any necessary authentication bootstrapping.
/// </summary>
public sealed class ValidationSessionBuilder : IValidationSessionBuilder
{
    private readonly IMcpHttpClient _httpClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IHealthCheckService _healthCheckService;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ISchemaValidator _schemaValidator;
    private readonly ILogger<ValidationSessionBuilder> _logger;

    public ValidationSessionBuilder(
        IMcpHttpClient httpClient,
        IAuthenticationService authenticationService,
        IHealthCheckService healthCheckService,
        ILogger<ValidationSessionBuilder> logger)
        : this(
            httpClient,
            authenticationService,
            healthCheckService,
            new EmbeddedSchemaRegistry(),
            new JsonSchemaValidator(),
            logger)
    {
    }

    public ValidationSessionBuilder(
        IMcpHttpClient httpClient,
        IAuthenticationService authenticationService,
        IHealthCheckService healthCheckService,
        ISchemaRegistry schemaRegistry,
        ISchemaValidator schemaValidator,
        ILogger<ValidationSessionBuilder> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ValidationSessionContext> BuildAsync(McpValidatorConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var server = CloneServerConfig(configuration.Server ?? throw new ValidationSessionException("Server configuration is required.", ValidationStatus.Failed));
        if (string.IsNullOrWhiteSpace(server.Endpoint))
        {
            throw new ValidationSessionException("Server endpoint is required before validation can begin.", ValidationStatus.Failed);
        }

        // STDIO Transport: If transport is stdio, the endpoint is the command to spawn.
        // The StdioMcpClientAdapter must be started before validation begins.
        // However, since we use DI-injected IMcpHttpClient, the caller (Program.cs) is
        // responsible for selecting the correct client implementation based on transport.
        // Here we just validate that the endpoint looks like a valid command for stdio.
        if (string.Equals(server.Transport, ValidationConstants.Transports.Stdio, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("STDIO transport detected; configured process command will remain redacted");
            if (server.Endpoint.StartsWith("http://") || server.Endpoint.StartsWith("https://"))
            {
                throw new ValidationSessionException(
                    "STDIO transport was specified but endpoint looks like an HTTP URL. " +
                    "For STDIO, provide the command to spawn (e.g., 'npx -y @modelcontextprotocol/server-filesystem /tmp').",
                    ValidationStatus.Failed);
            }
        }

        var (profile, profileSource) = ResolveServerProfile(server);
        server.Profile = profile;

        var context = new ValidationSessionContext(configuration, server)
        {
            ServerProfile = profile,
            ServerProfileSource = profileSource
        };

        context.SessionLogs.Add(new ValidationLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = CoreLogLevel.Information,
            Message = $"Server profile resolved to {profile} ({profileSource})."
        });

        await ConfigureInitialAuthenticationAsync(server, cancellationToken);

        _httpClient.SetProtocolVersion(server.ProtocolVersion);
        _httpClient.SetAuthentication(server.Authentication);

        var modernDiscoverySucceeded = false;
        if (ProtocolEraVersions.IsModern(server.ProtocolVersion))
        {
            context.ModernDiscovery = await CaptureModernDiscoveryAsync(server, cancellationToken);
            modernDiscoverySucceeded = context.ModernDiscovery.IsSuccessful && context.ModernDiscovery.Payload?.IsValid == true;
            if (modernDiscoverySucceeded)
            {
                _httpClient.SetModernDiscovery(context.ModernDiscovery.Payload);
            }
            if (!modernDiscoverySucceeded && server.ProtocolEra == McpProtocolEraSelection.Modern)
            {
                var reason = context.ModernDiscovery.Payload?.Errors.FirstOrDefault()
                    ?? context.ModernDiscovery.Error
                    ?? "server/discover did not return valid modern discovery evidence.";
                throw new ValidationSessionException($"Modern protocol discovery failed: {reason}", ValidationStatus.Failed);
            }
        }

        if (modernDiscoverySucceeded)
        {
            context.ProtocolVersion = server.ProtocolVersion;
            context.BootstrapHealth = new HealthCheckResult
            {
                IsHealthy = true,
                ProtocolVersion = server.ProtocolVersion,
                ErrorMessage = null
            };
        }
        else
        {
            var healthCheck = await _healthCheckService.PerformHealthCheckAsync(server, cancellationToken);
            context.BootstrapHealth = healthCheck;
            context.InitializationHandshake = healthCheck.InitializationDetails;

            if (!healthCheck.IsHealthy)
            {
                if (ValidationReliability.ShouldAllowDeferredValidation(healthCheck))
                {
                    _logger.LogInformation(
                        "Health check reported a deferred {Disposition} outcome that will be revisited during validation: {Message}",
                        healthCheck.Disposition,
                        healthCheck.ErrorMessage);
                }
                else
                {
                    throw new ValidationSessionException(healthCheck.ErrorMessage ?? "Server is unreachable", ValidationStatus.Failed);
                }
            }

            var negotiatedVersion = DetermineProtocolVersion(server, healthCheck);
            ValidateNegotiatedProtocolEra(server.ProtocolEra, negotiatedVersion);
            server.ProtocolVersion = negotiatedVersion;
            context.ProtocolVersion = negotiatedVersion;
        }

        _httpClient.SetProtocolVersion(server.ProtocolVersion);

        if (server.Authentication?.AllowInteractive == true)
        {
            context.AuthDiscovery = await EnsureAuthenticatedAsync(server, cancellationToken);

            if (context.AuthDiscovery != null)
            {
                PromoteProfileToAuthenticated(context, server, ServerProfileSource.Inferred, "Authentication challenge observed during session bootstrap");
            }
        }

        context.CapabilitySnapshot = await CaptureCapabilitySnapshotAsync(server, cancellationToken, context);

        // Best-effort: if we still don't know the effective MCP protocol version
        // after health check and authentication, try one more initialize
        // handshake under the final credentials to capture it for reporting.
        await TryRefreshProtocolVersionAsync(server, context, cancellationToken);

        return context;
    }

    private async Task ConfigureInitialAuthenticationAsync(McpServerConfig serverConfig, CancellationToken cancellationToken)
    {
        var auth = serverConfig.Authentication;
        if (auth?.Type != "device-code" || string.IsNullOrEmpty(auth.ClientId) || auth.Scopes == null)
        {
            return;
        }

        _logger.LogInformation("Initializing device code authentication flow...");
        var provider = new DeviceCodeAuthProvider(
            auth.ClientId,
            auth.Scopes,
            "User",
            auth.TenantId ?? "common");

        try
        {
            var token = await provider.GetAccessTokenAsync(cancellationToken);
            auth.Token = token;
            auth.Type = "bearer";
            _logger.LogInformation("Device code authentication completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device code authentication failed.");
            throw new ValidationSessionException($"Authentication failed: {ex.Message}", ValidationStatus.Failed, ex);
        }
    }

    private async Task<AuthDiscoveryInfo?> EnsureAuthenticatedAsync(McpServerConfig serverConfig, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Probing server authentication requirements...");
        var methodsToProbe = new[]
        {
            ValidationConstants.Methods.ToolsList,
            ValidationConstants.Methods.ResourcesList,
            ValidationConstants.Methods.PromptsList
        };

        foreach (var method in methodsToProbe)
        {
            var startTime = DateTime.UtcNow;
            var probeResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, method, null, cancellationToken);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var authChallenge = AuthenticationChallengeInterpreter.Inspect(probeResponse, duration);

            if (authChallenge.RequiresAuthentication)
            {
                var discoveryInfo = AuthenticationChallengeInterpreter.CreateDiscoveryInfo(authChallenge)
                    ?? new AuthDiscoveryInfo { DiscoveryTimeMs = duration };

                if (!authChallenge.IsAuthenticationChallenge)
                {
                    var metadataUrl = authChallenge.ResourceMetadataUrl;
                    return discoveryInfo;
                }

                {
                    var metadataUrl = authChallenge.ResourceMetadataUrl;
                    var isStandardOAuth = !string.IsNullOrWhiteSpace(authChallenge.AuthorizationUri) || authChallenge.UsesBearerChallenge;

                    if (!string.IsNullOrEmpty(metadataUrl))
                    {
                        try
                        {
                            var json = await _httpClient.GetStringAsync(metadataUrl, cancellationToken);
                            var authMetadata = JsonSerializer.Deserialize<AuthMetadata>(json);
                            if (authMetadata != null)
                            {
                                discoveryInfo.Metadata = authMetadata;
                                await AcquireTokenAsync(serverConfig, authMetadata, discoveryInfo, cancellationToken);
                                return discoveryInfo;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to fetch or parse OAuth metadata from {MetadataUrl}", metadataUrl);
                            discoveryInfo.Issues.Add($"Metadata fetch failed: {ex.Message}");
                        }
                    }

                    if (isStandardOAuth)
                    {
                        discoveryInfo.Issues.Add("Protected resource metadata was not available; falling back to challenge authorization hints.");

                        var authUri = "https://login.microsoftonline.com/common/v2.0";
                        if (!string.IsNullOrWhiteSpace(authChallenge.AuthorizationUri))
                        {
                            authUri = authChallenge.AuthorizationUri;
                        }

                        var syntheticMetadata = new AuthMetadata
                        {
                            AuthorizationServers = new List<string> { authUri },
                            ScopesSupported = new List<string> { "default" }
                        };

                        await AcquireTokenAsync(serverConfig, syntheticMetadata, discoveryInfo, cancellationToken);
                        return discoveryInfo;
                    }
                }

                return discoveryInfo;
            }
        }

        return null;
    }

    private async Task AcquireTokenAsync(McpServerConfig serverConfig, AuthMetadata metadata, AuthDiscoveryInfo discoveryInfo, CancellationToken cancellationToken)
    {
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authCts.CancelAfter(TimeSpan.FromMinutes(5));

        var token = await _authenticationService.GetTokenAsync(
            metadata,
            authCts.Token,
            isInteractive: true,
            tenantId: serverConfig.Authentication?.TenantId,
            clientId: serverConfig.Authentication?.ClientId);

        if (!string.IsNullOrEmpty(token))
        {
            discoveryInfo.Issues.Add("✅ Successfully authenticated via strategy-based flow");
            serverConfig.Authentication ??= new AuthenticationConfig();
            serverConfig.Authentication.Type = "bearer";
            serverConfig.Authentication.Token = token;
            _httpClient.SetAuthentication(serverConfig.Authentication);
        }
    }

    private async Task<TransportResult<CapabilitySummary>> CaptureCapabilitySnapshotAsync(McpServerConfig serverConfig, CancellationToken cancellationToken, ValidationSessionContext context)
    {
        try
        {
            return await _httpClient.ValidateCapabilitiesAsync(serverConfig.Endpoint!, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Capability snapshot capture failed.");
            var failure = new TransportResult<CapabilitySummary>
            {
                IsSuccessful = false,
                Error = ex.Message,
                Transport = TransportMetadata.Empty
            };

            context.SessionLogs.Add(new ValidationLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = CoreLogLevel.Warning,
                Category = TestCategory.ToolValidation,
                Message = $"Capability snapshot capture failed: {ex.Message}"
            });

            return failure;
        }
    }

    private async Task<TransportResult<ModernDiscoveryEvidence>> CaptureModernDiscoveryAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        JsonRpcResponse response;
        try
        {
            response = await _httpClient.CallAsync(
                serverConfig.Endpoint!,
                ValidationConstants.Methods.ServerDiscover,
                null,
                serverConfig.Authentication,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Modern server/discover response could not be bound; auto negotiation may fall back to initialize.");
            return CreateModernDiscoveryFailure(
                new JsonRpcResponse { StatusCode = -1, IsSuccess = false },
                "server/discover response could not be parsed as modern discovery evidence.");
        }
        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
        {
            return CreateModernDiscoveryFailure(
                response,
                response.Error ?? "server/discover returned no successful response.");
        }

        try
        {
            var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(_schemaRegistry, serverConfig.ProtocolVersion);
            if (!SchemaValidationHelpers.TryValidateResponseDefinition(
                    _schemaRegistry,
                    _schemaValidator,
                    protocolVersion,
                    SchemaValidationHelpers.DiscoverResultResponseDefinition,
                    response.RawJson,
                    _logger,
                    out var schemaResult) || schemaResult is null)
            {
                return CreateModernDiscoveryFailure(response, "The embedded server/discover response schema could not be evaluated.");
            }

            if (!schemaResult.IsValid)
            {
                return CreateModernDiscoveryFailure(response, string.Join(Environment.NewLine, schemaResult.Errors));
            }

            using var document = JsonDocument.Parse(response.RawJson);
            if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("server/discover response is missing an object result.");
            }

            var errors = new List<string>();
            var versions = ReadStringArray(result, "supportedVersions", errors);
            var resultType = ReadRequiredString(result, "resultType", errors);
            var cacheScope = ReadRequiredString(result, "cacheScope", errors);
            long? ttlMs = null;
            if (!result.TryGetProperty("ttlMs", out var ttlElement) || !ttlElement.TryGetInt64(out var parsedTtl) || parsedTtl < 0)
            {
                errors.Add("server/discover result requires a non-negative ttlMs.");
            }
            else
            {
                ttlMs = parsedTtl;
            }

            var capabilityNames = new List<string>();
            var extensionIds = new List<string>();
            if (!result.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
            {
                errors.Add("server/discover result requires an object capabilities field.");
            }
            else
            {
                capabilityNames.AddRange(capabilities.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
                if (capabilities.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object)
                {
                    extensionIds.AddRange(extensions.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
                }
            }

            if (!versions.Contains(serverConfig.ProtocolVersion ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add($"server/discover supportedVersions does not include selected version '{serverConfig.ProtocolVersion}'.");
            }

            return new TransportResult<ModernDiscoveryEvidence>
            {
                IsSuccessful = true,
                Payload = new ModernDiscoveryEvidence
                {
                    IsValid = errors.Count == 0,
                    SupportedVersions = versions,
                    CapabilityNames = capabilityNames,
                    ExtensionIds = extensionIds,
                    ResultType = resultType,
                    CacheScope = cacheScope,
                    TtlMs = ttlMs,
                    Instructions = result.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String
                        ? instructions.GetString()
                        : null,
                    Errors = errors
                },
                Transport = CreateTransportMetadata(response)
            };
        }
        catch (JsonException ex)
        {
            return new TransportResult<ModernDiscoveryEvidence>
            {
                IsSuccessful = false,
                Error = ex.Message,
                Transport = CreateTransportMetadata(response)
            };
        }
    }

    private static TransportResult<ModernDiscoveryEvidence> CreateModernDiscoveryFailure(JsonRpcResponse response, string error)
    {
        return new TransportResult<ModernDiscoveryEvidence>
        {
            IsSuccessful = false,
            Error = error,
            Payload = new ModernDiscoveryEvidence { IsValid = false, Errors = [error] },
            Transport = CreateTransportMetadata(response)
        };
    }

    private static TransportMetadata CreateTransportMetadata(JsonRpcResponse response)
    {
        return new TransportMetadata
        {
            StatusCode = response.StatusCode,
            Headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase),
            Duration = TimeSpan.FromMilliseconds(response.ElapsedMs ?? 0),
            RawContent = response.RawJson
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string propertyName, List<string> errors)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"server/discover result requires an array {propertyName} field.");
            return Array.Empty<string>();
        }

        var values = element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (values.Length != element.GetArrayLength() || values.Length == 0)
        {
            errors.Add($"server/discover result {propertyName} must contain non-empty unique strings.");
        }

        return values;
    }

    private static string? ReadRequiredString(JsonElement parent, string propertyName, List<string> errors)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            errors.Add($"server/discover result requires a non-empty {propertyName} field.");
            return null;
        }

        return element.GetString();
    }

    private static (McpServerProfile profile, ServerProfileSource source) ResolveServerProfile(McpServerConfig server)
    {
        if (server.Profile != McpServerProfile.Unspecified)
        {
            return (server.Profile, ServerProfileSource.UserDeclared);
        }

        var auth = server.Authentication;
        if (auth?.Required == true || McpAuthenticationHelper.HasCredential(auth) || auth?.AllowInteractive == true)
        {
            return (McpServerProfile.Authenticated, ServerProfileSource.Inferred);
        }

        return (McpServerProfile.Public, ServerProfileSource.Inferred);
    }

    private static void PromoteProfileToAuthenticated(
        ValidationSessionContext context,
        McpServerConfig server,
        ServerProfileSource source,
        string? reason = null)
    {
        if (context.ServerProfile == McpServerProfile.Authenticated || context.ServerProfile == McpServerProfile.Enterprise)
        {
            return;
        }

        context.ServerProfile = McpServerProfile.Authenticated;
        context.ServerProfileSource = source;
        server.Profile = context.ServerProfile;

        context.SessionLogs.Add(new ValidationLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = CoreLogLevel.Information,
            Message = reason == null
                ? "Profile promoted to Authenticated based on observed behavior."
                : $"Profile promoted to Authenticated: {reason}"
        });
    }

    private static string? DetermineProtocolVersion(McpServerConfig serverConfig, HealthCheckResult healthCheck)
    {
        var negotiatedVersion = healthCheck.InitializationDetails?.Payload?.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(negotiatedVersion))
        {
            return negotiatedVersion;
        }

        if (!string.IsNullOrWhiteSpace(healthCheck.ProtocolVersion) &&
            !string.Equals(healthCheck.ProtocolVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return healthCheck.ProtocolVersion;
        }

        return serverConfig.ProtocolVersion;
    }

    private static void ValidateNegotiatedProtocolEra(McpProtocolEraSelection selection, string? negotiatedVersion)
    {
        if (selection == McpProtocolEraSelection.Auto)
        {
            return;
        }

        if (!SchemaRegistryProtocolVersions.IsAvailableVersion(negotiatedVersion))
        {
            throw new ValidationSessionException(
                $"The server negotiated unsupported protocol version '{negotiatedVersion ?? "unknown"}' for explicit {selection.ToString().ToLowerInvariant()} era validation.",
                ValidationStatus.Failed);
        }

        var negotiatedModern = ProtocolEraVersions.IsModern(negotiatedVersion);
        var matches = selection == McpProtocolEraSelection.Modern ? negotiatedModern : !negotiatedModern;
        if (!matches)
        {
            throw new ValidationSessionException(
                $"The server negotiated protocol version '{negotiatedVersion}', which conflicts with explicit {selection.ToString().ToLowerInvariant()} era validation.",
                ValidationStatus.Failed);
        }
    }

    private async Task TryRefreshProtocolVersionAsync(McpServerConfig serverConfig, ValidationSessionContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(context.ProtocolVersion))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            return;
        }

        try
        {
            _logger.LogDebug("Attempting post-auth initialize handshake to resolve protocol version for {Endpoint}", serverConfig.Endpoint);
            var initResult = await _httpClient.ValidateInitializeAsync(serverConfig.Endpoint, cancellationToken);

            if (initResult.IsSuccessful && !string.IsNullOrWhiteSpace(initResult.Payload?.ProtocolVersion))
            {
                var version = initResult.Payload.ProtocolVersion;
                context.ProtocolVersion = version;
                serverConfig.ProtocolVersion = version;
                context.InitializationHandshake = initResult;
                _logger.LogInformation("Protocol version resolved via post-auth initialize handshake: {Version}", version);

                // Ensure subsequent calls advertise the negotiated version.
                _httpClient.SetProtocolVersion(version);
            }
            else
            {
                _logger.LogDebug("Post-auth initialize handshake completed without a usable protocol version (success={IsSuccessful}).", initResult.IsSuccessful);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to refresh protocol version via post-auth initialize handshake for {Endpoint}.", serverConfig.Endpoint);
        }
    }

    private static McpServerConfig CloneServerConfig(McpServerConfig source)
    {
        return source.CloneForExecution();
    }
}
