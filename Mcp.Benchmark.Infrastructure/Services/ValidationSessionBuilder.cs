using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
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
    private readonly ILogger<ValidationSessionBuilder> _logger;

    public ValidationSessionBuilder(
        IMcpHttpClient httpClient,
        IAuthenticationService authenticationService,
        IHealthCheckService healthCheckService,
        ILogger<ValidationSessionBuilder> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
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
            _logger.LogInformation("STDIO transport detected. Endpoint will be treated as process command: {Command}", server.Endpoint);
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

        var healthCheck = await _healthCheckService.PerformHealthCheckAsync(server, cancellationToken);
        context.InitializationHandshake = healthCheck.InitializationDetails;

        if (!healthCheck.IsHealthy)
        {
            if (IsSoftHealthFailure(healthCheck.ErrorMessage))
            {
                _logger.LogInformation("Health check reported a soft failure that will be re-tried later: {Message}", healthCheck.ErrorMessage);
            }
            else
            {
                throw new ValidationSessionException(healthCheck.ErrorMessage ?? "Server is unreachable", ValidationStatus.Failed);
            }
        }

        var negotiatedVersion = DetermineProtocolVersion(server, healthCheck);
        server.ProtocolVersion = negotiatedVersion;
        context.ProtocolVersion = negotiatedVersion;

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

            if (probeResponse.StatusCode == 401 || probeResponse.StatusCode == 403)
            {
                var discoveryInfo = new AuthDiscoveryInfo { DiscoveryTimeMs = duration };
                string? headerValue = null;
                if (probeResponse.Headers != null)
                {
                    if (probeResponse.Headers.TryGetValue("WWW-Authenticate", out var header)) headerValue = header.ToString();
                    else if (probeResponse.Headers.TryGetValue("www-authenticate", out var lowerHeader)) headerValue = lowerHeader.ToString();
                }

                discoveryInfo.WwwAuthenticateHeader = headerValue;

                var isAzureHost = serverConfig.Endpoint?.Contains("azurecontainerapps.io", StringComparison.OrdinalIgnoreCase) == true ||
                                  serverConfig.Endpoint?.Contains("azurewebsites.net", StringComparison.OrdinalIgnoreCase) == true;

                if (!string.IsNullOrEmpty(headerValue) || isAzureHost)
                {
                    string? metadataUrl = null;
                    var isStandardOAuth = false;
                    var forceAzure = isAzureHost && string.IsNullOrEmpty(headerValue);

                    if (!forceAzure && headerValue!.Contains("resource_metadata=\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var marker = "resource_metadata=\"";
                        var start = headerValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
                        var end = headerValue.IndexOf('\"', start);
                        if (end > start)
                        {
                            metadataUrl = headerValue.Substring(start, end - start);
                        }
                    }
                    else if (forceAzure || headerValue!.Contains("authorization_uri=\"", StringComparison.OrdinalIgnoreCase) || headerValue.Contains("Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        isStandardOAuth = true;
                    }

                    if (isStandardOAuth)
                    {
                        var authUri = "https://login.microsoftonline.com/common/v2.0";
                        if (!forceAzure && headerValue!.Contains("authorization_uri=\"", StringComparison.OrdinalIgnoreCase))
                        {
                            var marker = "authorization_uri=\"";
                            var start = headerValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
                            var end = headerValue.IndexOf('\"', start);
                            if (end > start)
                            {
                                authUri = headerValue.Substring(start, end - start);
                            }
                        }

                        var syntheticMetadata = new AuthMetadata
                        {
                            AuthorizationServers = new List<string> { authUri },
                            ScopesSupported = new List<string> { "default" }
                        };

                        await AcquireTokenAsync(serverConfig, syntheticMetadata, discoveryInfo, cancellationToken);
                        return discoveryInfo;
                    }

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
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to fetch or parse OAuth metadata from {MetadataUrl}", metadataUrl);
                            discoveryInfo.Issues.Add($"Metadata fetch failed: {ex.Message}");
                        }
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

    private static (McpServerProfile profile, ServerProfileSource source) ResolveServerProfile(McpServerConfig server)
    {
        if (server.Profile != McpServerProfile.Unspecified)
        {
            return (server.Profile, ServerProfileSource.UserDeclared);
        }

        var auth = server.Authentication;
        if (auth?.Required == true || !string.IsNullOrWhiteSpace(auth?.Token) || auth?.AllowInteractive == true)
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

    private static bool IsSoftHealthFailure(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        var message = errorMessage.ToLowerInvariant();
        return message.Contains("401") || message.Contains("403") || message.Contains("405") || message.Contains("method not allowed");
    }

    private static McpServerConfig CloneServerConfig(McpServerConfig source)
    {
        var clone = new McpServerConfig
        {
            Endpoint = source.Endpoint,
            Transport = source.Transport,
            ProtocolVersion = source.ProtocolVersion,
            TimeoutMs = source.TimeoutMs,
            Authentication = CloneAuthentication(source.Authentication),
            Headers = new Dictionary<string, string>(source.Headers ?? new Dictionary<string, string>()),
            Environment = new Dictionary<string, string>(source.Environment ?? new Dictionary<string, string>())
        };

        return clone;
    }

    private static AuthenticationConfig? CloneAuthentication(AuthenticationConfig? source)
    {
        if (source == null)
        {
            return null;
        }

        return new AuthenticationConfig
        {
            Type = source.Type,
            Required = source.Required,
            Token = source.Token,
            Username = source.Username,
            Password = source.Password,
            ClientId = source.ClientId,
            TenantId = source.TenantId,
            Scopes = source.Scopes,
            Authority = source.Authority,
            CustomHeaders = new Dictionary<string, string>(source.CustomHeaders ?? new Dictionary<string, string>()),
            AllowInteractive = source.AllowInteractive
        };
    }
}
