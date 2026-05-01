using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Utilities;
using Mcp.Compliance.Spec;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ValidatorJsonRpcRequest = Mcp.Benchmark.Core.Models.JsonRpcRequest;
using ValidatorJsonRpcResponse = Mcp.Benchmark.Core.Models.JsonRpcResponse;

namespace Mcp.Benchmark.Infrastructure.Http;

/// <summary>
/// Real HTTP-based MCP client for actual server validation.
/// </summary>
public class McpHttpClient : IMcpHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpHttpClient> _logger;
    private readonly IMcpClient _mcpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _concurrencyLock = new();
    private SemaphoreSlim? _requestSemaphore;
    private int _maxConcurrency;
    private string? _protocolVersion;
    private AuthenticationConfig? _defaultAuthentication;
    private ExecutionPolicy? _executionPolicy;
    private int _requestCount;
    private string? _sessionId;
    private const int DefaultRequestTimeoutSeconds = 60;
    private const string ProtocolVersionHeaderName = "MCP-Protocol-Version";
    private const string SessionIdHeaderName = "MCP-Session-Id";

    public McpHttpClient(HttpClient httpClient, ILogger<McpHttpClient> logger, IMcpClient mcpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        SetConcurrencyLimit(Environment.ProcessorCount);
    }

    /// <summary>
    /// Sets the authentication configuration for subsequent requests.
    /// </summary>
    public void SetAuthentication(AuthenticationConfig? authentication)
    {
        _defaultAuthentication = CloneAuthentication(authentication);

        if (authentication == null)
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            return;
        }

        if (string.Equals(authentication.Type, "bearer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(authentication.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authentication.Token);
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    public void SetConcurrencyLimit(int maxConcurrency)
    {
        var normalized = Math.Clamp(maxConcurrency, 1, 256);

        lock (_concurrencyLock)
        {
            if (_requestSemaphore != null && _maxConcurrency == normalized)
            {
                return;
            }

            _requestSemaphore = new SemaphoreSlim(normalized, normalized);
            _maxConcurrency = normalized;
        }

        _logger.LogDebug("HTTP concurrency limit set to {MaxConcurrency}", normalized);
    }

    /// <summary>
    /// Sets the MCP protocol version that should be advertised to the
    /// server for subsequent JSON-RPC requests.
    /// </summary>
    /// <param name="protocolVersion">The protocol version string to advertise, or null to clear.</param>
    public void SetProtocolVersion(string? protocolVersion)
    {
        _protocolVersion = string.IsNullOrWhiteSpace(protocolVersion)
            ? null
            : protocolVersion.Trim();
    }

    public void ConfigureExecutionPolicy(ExecutionPolicy? executionPolicy)
    {
        _executionPolicy = executionPolicy?.Clone();
        Interlocked.Exchange(ref _requestCount, 0);

        if (_executionPolicy == null)
        {
            return;
        }

        SetConcurrencyLimit(Math.Max(1, _executionPolicy.MaxConcurrency));
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _executionPolicy.TimeoutSeconds));
    }

    /// <summary>
    /// Performs a GET request to the specified URL and returns the response body as a string.
    /// </summary>
    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        EnforceExecutionPolicy(url);
        return await _httpClient.GetStringAsync(url, cancellationToken);
    }

    /// <summary>
    /// Makes an actual JSON-RPC 2.0 call to the MCP server.
    /// </summary>
    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return await CallAsync(endpoint, method, parameters, null, cancellationToken);
    }

    /// <summary>
    /// Makes an actual JSON-RPC 2.0 call to the MCP server with authentication configuration.
    /// </summary>
    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        var request = new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = parameters,
            Id = requestId
        };

        _logger.LogDebug("Making JSON-RPC call to {Endpoint}: {Method} with auth: {HasAuth}", endpoint, method, authentication != null);
        
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        
        return await ExecuteWithRetryAsync(endpoint, json, authentication, method, requestId, cancellationToken);
    }

    private async Task<ValidatorJsonRpcResponse> ExecuteWithRetryAsync(string endpoint, string jsonPayload, AuthenticationConfig? authentication, string? method, string? requestId, CancellationToken cancellationToken)
    {
        var maxAttempts = ValidationReliability.DefaultRpcMaxAttempts;
        var attempt = 0;
        double? lastAttemptElapsedMs = null;
        var authApplied = false;
        string? authScheme = null;

        var throttle = _requestSemaphore ?? throw new InvalidOperationException("HTTP concurrency limiter not initialized.");
        try
        {
            await throttle.WaitAsync(cancellationToken);

            while (true)
            {
                attempt++;
                var content = CreateJsonContent(jsonPayload);

                // Create a new HttpRequestMessage to set authentication headers
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };

                // Add Accept header (Best Practice)
                requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

                ApplyProtocolVersionHeader(requestMessage);
                ApplySessionIdHeader(requestMessage, ShouldIncludeSessionHeader(method));

                // Apply authentication if provided
                if (authentication != null)
                {
                    var headerValue = McpAuthenticationHelper.BuildAuthorizationHeaderValue(authentication);

                    if (headerValue is null)
                    {
                        // No auth
                        _logger.LogDebug("No authentication provided - making unauthenticated request");
                    }
                    else if (headerValue.Length == 0)
                    {
                        // Explicitly provided authentication config with empty token means "No Auth"
                        // We do NOT fall back to global auth in this case
                        _logger.LogDebug("Explicit empty token provided - making unauthenticated request");
                    }
                    else
                    {
                        McpAuthenticationHelper.ApplyAuthorizationHeader(requestMessage.Headers, headerValue);
                        authApplied = true;
                        authScheme = requestMessage.Headers.Authorization?.Scheme ?? authentication.Type;
                        _logger.LogDebug("Added authentication header");
                    }
                }
                else if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    // Fallback to global auth if set via SetAuthentication
                    requestMessage.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
                    authApplied = true;
                    authScheme = requestMessage.Headers.Authorization.Scheme;
                }
                else
                {
                    _logger.LogDebug("No authentication provided - making unauthenticated request");
                }

                // Measure only the actual HTTP request/response cycle for
                // latency metrics. This intentionally excludes validator
                // backoff delays between retries and any external
                // scheduling overhead.
                HttpResponseMessage httpResponse;
                try
                {
                    EnforceExecutionPolicy(endpoint);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    httpResponse = await _httpClient.SendAsync(requestMessage, cancellationToken);
                    sw.Stop();
                    lastAttemptElapsedMs = sw.Elapsed.TotalMilliseconds;
                }
                catch (Exception ex) when (ValidationReliability.ShouldRetryException(ex, cancellationToken) && attempt < maxAttempts)
                {
                    var delay = ValidationReliability.GetRetryDelay(attempt);
                    _logger.LogWarning(ex, "Transient transport failure. Retrying in {Delay}s (Attempt {Attempt}/{MaxAttempts})...", delay.TotalSeconds, attempt, maxAttempts);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if (ValidationReliability.IsRetryableHttpStatusCode((int)httpResponse.StatusCode) && attempt < maxAttempts)
                {
                    var responseHeaders = httpResponse.Headers.ToDictionary(
                        header => header.Key,
                        header => string.Join(",", header.Value),
                        StringComparer.OrdinalIgnoreCase);
                    var delay = httpResponse.Headers.RetryAfter?.Delta ?? ValidationReliability.GetRetryDelay(attempt, responseHeaders);

                    if ((int)httpResponse.StatusCode == (int)HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Rate limited (429). Retrying in {Delay}s (Attempt {Attempt}/{MaxAttempts})...", delay.TotalSeconds, attempt, maxAttempts);
                    }
                    else
                    {
                        _logger.LogWarning("Transient HTTP {StatusCode}. Retrying in {Delay}s (Attempt {Attempt}/{MaxAttempts})...", (int)httpResponse.StatusCode, delay.TotalSeconds, attempt, maxAttempts);
                    }

                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var responseJson = await ReadResponseContentAsync(httpResponse.Content, cancellationToken);

                _logger.LogDebug("Received response: {Status} - {Response}", httpResponse.StatusCode, 
                    responseJson?.Length > 1000 ? responseJson.Substring(0, 1000) + "..." : responseJson);

                // Capture all headers (both response headers and content headers)
                var allHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in httpResponse.Headers)
                {
                    allHeaders[header.Key] = string.Join(",", header.Value);
                }
                foreach (var header in httpResponse.Content.Headers)
                {
                    allHeaders[header.Key] = string.Join(",", header.Value);
                }

                string? errorMessage = null;
                if (!httpResponse.IsSuccessStatusCode)
                {
                    errorMessage = $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}. Body: {responseJson}";
                }

                CaptureHttpStateFromResponse(method, responseJson, allHeaders);

                return new ValidatorJsonRpcResponse
                {
                    StatusCode = (int)httpResponse.StatusCode,
                    IsSuccess = httpResponse.IsSuccessStatusCode,
                    RawJson = responseJson,
                    Headers = allHeaders,
                    Error = errorMessage,
                    ElapsedMs = lastAttemptElapsedMs,
                    ProbeContext = CreateProbeContext(
                        method,
                        requestId,
                        _protocolVersion,
                        authApplied,
                        authScheme,
                        (int)httpResponse.StatusCode,
                        httpResponse.IsSuccessStatusCode,
                        errorMessage,
                        allHeaders)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making JSON-RPC call to {Endpoint}", endpoint);

            return new ValidatorJsonRpcResponse
            {
                StatusCode = -1,
                IsSuccess = false,
                Error = ex.Message,
                RawJson = null,
                ElapsedMs = null,
                ProbeContext = CreateProbeContext(
                    method,
                    requestId,
                    _protocolVersion,
                    authApplied,
                    authScheme,
                    -1,
                    false,
                    ex.Message,
                    null)
            };
        }
        finally
        {
            // Only release the semaphore if it was successfully acquired. If WaitAsync
            // throws (e.g., due to cancellation), we must not call Release, otherwise
            // we will eventually hit "adding the specified count to the semaphore would
            // cause it to exceed its maximum count".
            if (throttle.CurrentCount < _maxConcurrency)
            {
                throttle.Release();
            }
        }
    }

    /// <summary>
    /// Tests if server returns proper JSON-RPC error codes.
    /// </summary>
    public async Task<JsonRpcErrorValidationResult> ValidateErrorCodesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var results = new List<JsonRpcErrorTest>();

        // Test Parse Error (-32700) - Invalid JSON
        var parseErrorResponse = await SendRawJsonAsync(endpoint, "{ invalid json", cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Parse Error",
            Payload = "{ invalid json",
            ExpectedErrorCode = -32700,
            ActualResponse = parseErrorResponse,
            IsValid = CheckErrorCode(parseErrorResponse, -32700)
        });

        // Test Invalid Request (-32600) - Missing jsonrpc field
        var invalidRequestResponse = await SendRawJsonAsync(endpoint, 
            "{\"method\":\"test\",\"id\":1,\"params\":{}}", cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Invalid Request - Missing jsonrpc",
            Payload = "{\"method\":\"test\",\"id\":1,\"params\":{}}",
            ExpectedErrorCode = -32600,
            ActualResponse = invalidRequestResponse,
            IsValid = CheckErrorCode(invalidRequestResponse, -32600)
        });

        // Test Method Not Found (-32601)
        var methodNotFoundResponse = await CallAsync(endpoint, "nonexistent_method_12345", null, cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Method Not Found",
            ExpectedErrorCode = -32601,
            ActualResponse = methodNotFoundResponse,
            IsValid = CheckErrorCode(methodNotFoundResponse, -32601)
        });

        // Test Invalid Params (-32602)
        // Sending a string as params is technically an Invalid Request (-32600) per spec because params must be Array/Object.
        // However, some servers might return -32602. We accept either for this test case to be robust.
        var invalidParamsResponse = await CallAsync(endpoint, "tools/call", "invalid_params_string", cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Invalid Params",
            ExpectedErrorCode = -32602,
            ActualResponse = invalidParamsResponse,
            IsValid = CheckErrorCode(invalidParamsResponse, -32602) || CheckErrorCode(invalidParamsResponse, -32600)
        });

        return new JsonRpcErrorValidationResult
        {
            Tests = results,
            OverallScore = results.Count(r => r.IsValid) / (double)results.Count * 100,
            IsCompliant = results.All(r => r.IsValid)
        };
    }

    public async Task<TransportResilienceProbeResult> ProbeTimeoutRecoveryAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = new TransportResilienceProbeResult
        {
            ProbeId = "timeout-handling",
            DisplayName = "Timeout Handling Recovery",
            ExpectedOutcome = "A validator-induced HTTP timeout should abort the request and the endpoint should still respond to a follow-up MCP probe."
        };

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(25));

        var payload = CreateProbePayload("rpc.timeout.probe");
        result.Executed = true;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var requestMessage = CreatePostRequestMessage(
                endpoint,
                new DelayedAbortJsonContent(payload, TimeSpan.FromMilliseconds(250)),
                authentication: null,
                acceptWildcard: true);

            using var response = await _httpClient.SendAsync(requestMessage, timeoutTokenSource.Token);
            sw.Stop();

            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.FailureResponse = new ValidatorJsonRpcResponse
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                RawJson = await ReadResponseContentAsync(response.Content, cancellationToken),
                ElapsedMs = result.FailureElapsedMs
            };
            result.ActualOutcome = "Timeout probe completed before the induced cancellation window elapsed.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            result.FailureObserved = true;
            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.FailureResponse = CreateSyntheticFailureResponse("HTTP request cancelled by validator-induced timeout.", result.FailureElapsedMs);
            result.ActualOutcome = "Validator cancelled the HTTP request after the induced timeout window elapsed.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.ActualOutcome = $"Timeout probe failed unexpectedly: {ex.Message}";
            result.Notes.Add(ex.Message);
        }

        await PopulateRecoveryProbeAsync(endpoint, result, cancellationToken);
        return result;
    }

    public async Task<TransportResilienceProbeResult> ProbeConnectionInterruptionRecoveryAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = new TransportResilienceProbeResult
        {
            ProbeId = "connection-interruption",
            DisplayName = "Connection Interruption Recovery",
            ExpectedOutcome = "A validator-induced HTTP connection interruption should be observed and the endpoint should still respond to a follow-up MCP probe."
        };

        var payload = CreateProbePayload("rpc.connection.interruption.probe");
        result.Executed = true;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var requestMessage = CreatePostRequestMessage(
                endpoint,
                new InterruptingJsonContent(payload),
                authentication: null,
                acceptWildcard: true);

            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            sw.Stop();

            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.FailureResponse = new ValidatorJsonRpcResponse
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                RawJson = await ReadResponseContentAsync(response.Content, cancellationToken),
                ElapsedMs = result.FailureElapsedMs
            };
            result.ActualOutcome = "Connection interruption probe completed without the transport being interrupted.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            sw.Stop();
            result.FailureObserved = true;
            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.FailureResponse = CreateSyntheticFailureResponse($"HTTP connection interrupted by validator: {ex.Message}", result.FailureElapsedMs);
            result.ActualOutcome = "Validator aborted the HTTP request body mid-stream to simulate a connection interruption.";
        }

        await PopulateRecoveryProbeAsync(endpoint, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Tests the MCP initialize handshake.
    /// </summary>
    public async Task<TransportResult<InitializeResult>> ValidateInitializeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing MCP initialize handshake for {Endpoint}", endpoint);

        var startTime = DateTime.UtcNow;
        try
        {
            var serverConfig = BuildServerConfig(endpoint);

            try
            {
                var initializeResult = await _mcpClient
                    .InitializeAsync(serverConfig, null, _protocolVersion, cancellationToken)
                    .ConfigureAwait(false);

                return new TransportResult<InitializeResult>
                {
                    IsSuccessful = true,
                    Payload = initializeResult,
                    Transport = CreateTransportMetadata(DateTime.UtcNow - startTime)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SDK initialize failed for {Endpoint}; falling back to raw JSON-RPC initialize", endpoint);
            }

            var initializeResponse = await CallAsync(endpoint, "initialize", new
            {
                protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(_protocolVersion),
                capabilities = new { },
                clientInfo = new
                {
                    name = "mcp-benchmark",
                    version = "1.0.0"
                }
            }, cancellationToken).ConfigureAwait(false);

            if (!initializeResponse.IsSuccess)
            {
                return new TransportResult<InitializeResult>
                {
                    IsSuccessful = false,
                    Error = initializeResponse.Error ?? $"HTTP {initializeResponse.StatusCode}",
                    Transport = CreateTransportMetadata(
                        DateTime.UtcNow - startTime,
                        initializeResponse.StatusCode,
                        initializeResponse.Headers,
                        initializeResponse.RawJson)
                };
            }

            if (!TryParseInitializeResult(initializeResponse.RawJson, out var parsedInitializeResult))
            {
                return new TransportResult<InitializeResult>
                {
                    IsSuccessful = false,
                    Error = "Initialize response could not be parsed.",
                    Transport = CreateTransportMetadata(
                        DateTime.UtcNow - startTime,
                        initializeResponse.StatusCode,
                        initializeResponse.Headers,
                        initializeResponse.RawJson)
                };
            }

            return new TransportResult<InitializeResult>
            {
                IsSuccessful = true,
                    Payload = parsedInitializeResult,
                Transport = CreateTransportMetadata(
                    DateTime.UtcNow - startTime,
                    initializeResponse.StatusCode,
                    initializeResponse.Headers,
                    initializeResponse.RawJson)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP initialize handshake failed for {Endpoint}", endpoint);
            return new TransportResult<InitializeResult>
            {
                IsSuccessful = false,
                Error = ex.Message,
                Transport = CreateTransportMetadata(DateTime.UtcNow - startTime)
            };
        }
    }

    /// <summary>
    /// Validates declared capabilities match actual implementation.
    /// </summary>
    public async Task<TransportResult<CapabilitySummary>> ValidateCapabilitiesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var initializeResult = await ValidateInitializeAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var capabilityDeclarationsAvailable = CapabilitySnapshotUtils.HasCapabilityDeclarations(initializeResult.Payload);
        var advertisedCapabilities = CapabilitySnapshotUtils.ExtractAdvertisedCapabilities(initializeResult.Payload);
        var shouldProbeTools = CapabilitySnapshotUtils.ShouldProbeCapability(
            capabilityDeclarationsAvailable,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Tools);
        var shouldProbeResources = CapabilitySnapshotUtils.ShouldProbeCapability(
            capabilityDeclarationsAvailable,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Resources);
        var shouldProbePrompts = CapabilitySnapshotUtils.ShouldProbeCapability(
            capabilityDeclarationsAvailable,
            advertisedCapabilities,
            McpSpecConstants.Capabilities.Prompts);

        IReadOnlyList<McpClientTool> discoveredTools = Array.Empty<McpClientTool>();
        var toolListingSucceeded = false;
        var toolInvocationSucceeded = false;
        string? firstToolName = null;

        var serverConfig = BuildServerConfig(endpoint);

        if (shouldProbeTools)
        {
            try
            {
                discoveredTools = await _mcpClient
                    .ListToolsAsync(serverConfig, null, _protocolVersion, cancellationToken)
                    .ConfigureAwait(false);

                toolListingSucceeded = true;
                firstToolName = discoveredTools.FirstOrDefault()?.Name;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SDK tool discovery failed for {Endpoint}; falling back to raw JSON-RPC probes", endpoint);
            }
        }

        var (toolListResponse, toolListDuration) = shouldProbeTools
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ToolsList, cancellationToken)
            : (null, 0);
        var (resourceListResponse, resourceListDuration) = shouldProbeResources
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ResourcesList, cancellationToken)
            : (null, 0);
        var (promptListResponse, promptListDuration) = shouldProbePrompts
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.PromptsList, cancellationToken)
            : (null, 0);

        var rawDiscoveredToolCount = CountCollectionItems(toolListResponse, "tools");
        var resourceListingSucceeded = resourceListResponse?.IsSuccess == true;
        var promptListingSucceeded = promptListResponse?.IsSuccess == true;

        if (!toolListingSucceeded && toolListResponse?.IsSuccess == true)
        {
            toolListingSucceeded = true;
        }

        if (toolListingSucceeded && string.IsNullOrWhiteSpace(firstToolName) && rawDiscoveredToolCount > 0)
        {
            firstToolName = TryGetFirstToolName(toolListResponse?.RawJson);
        }

        if (toolListingSucceeded && !string.IsNullOrWhiteSpace(firstToolName))
        {
            try
            {
                var toolCallResponse = await CallAsync(
                    endpoint,
                    ValidationConstants.Methods.ToolsCall,
                    new
                    {
                        name = firstToolName,
                        arguments = new { }
                    },
                    cancellationToken).ConfigureAwait(false);

                toolInvocationSucceeded = toolCallResponse.IsSuccess || toolCallResponse.StatusCode == 400;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calling tool {Tool}", firstToolName);
                toolInvocationSucceeded = false;
            }
        }

        var payload = BuildSummary();
        var capabilitySnapshotSucceeded = (initializeResult.IsSuccessful && capabilityDeclarationsAvailable) ||
            toolListingSucceeded ||
            resourceListingSucceeded ||
            promptListingSucceeded;

        return new TransportResult<CapabilitySummary>
        {
            IsSuccessful = capabilitySnapshotSucceeded,
            Error = capabilitySnapshotSucceeded ? null : toolListResponse?.Error ?? initializeResult.Error ?? "Capability discovery failed",
            Payload = payload,
            Transport = CreateTransportMetadata(
                ResolveTransportDuration(),
                toolListResponse?.StatusCode ?? initializeResult.Transport.StatusCode,
                toolListResponse?.Headers ?? initializeResult.Transport.Headers,
                toolListResponse?.RawJson ?? initializeResult.Transport.RawContent)
        };

        CapabilitySummary BuildSummary()
        {
            var discoveredToolCount = discoveredTools.Count > 0
                ? discoveredTools.Count
                : rawDiscoveredToolCount;

            return new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = capabilityDeclarationsAvailable,
                AdvertisedCapabilities = advertisedCapabilities,
                Tools = discoveredTools,
                ToolListingSucceeded = toolListingSucceeded,
                ToolInvocationSucceeded = toolInvocationSucceeded,
                FirstToolName = firstToolName,
                DiscoveredToolsCount = discoveredToolCount,
                Score = CalculateCapabilityScore(
                    capabilityDeclarationsAvailable,
                    advertisedCapabilities,
                    toolListingSucceeded,
                    toolInvocationSucceeded,
                    resourceListingSucceeded,
                    promptListingSucceeded),
                ToolListResponse = toolListResponse,
                ResourceListResponse = resourceListResponse,
                PromptListResponse = promptListResponse,
                ToolListDurationMs = toolListDuration,
                ResourceListDurationMs = resourceListDuration,
                PromptListDurationMs = promptListDuration,
                ResourceListingSucceeded = resourceListingSucceeded,
                PromptListingSucceeded = promptListingSucceeded,
                DiscoveredResourcesCount = CountCollectionItems(resourceListResponse, "resources"),
                DiscoveredPromptsCount = CountCollectionItems(promptListResponse, "prompts")
            };
        }

        TimeSpan ResolveTransportDuration()
        {
            if (toolListDuration > 0)
            {
                return TimeSpan.FromMilliseconds(toolListDuration);
            }

            return DateTime.UtcNow - startTime;
        }

        async Task<(ValidatorJsonRpcResponse? Response, double Duration)> ProbeJsonRpcAsync(string serverEndpoint, string method, CancellationToken token)
        {
            try
            {
                var methodStart = DateTime.UtcNow;
                var response = await CallAsync(serverEndpoint, method, null, token).ConfigureAwait(false);
                var duration = (DateTime.UtcNow - methodStart).TotalMilliseconds;
                return (response, duration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JSON-RPC probe {Method} failed for {Endpoint}", method, serverEndpoint);
                return (null, 0);
            }
        }
    }

    private int CountCollectionItems(ValidatorJsonRpcResponse? response, string collectionPropertyName)
    {
        if (string.IsNullOrWhiteSpace(response?.RawJson))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(response.RawJson);
            if (document.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty(collectionPropertyName, out var collectionElement) &&
                collectionElement.ValueKind == JsonValueKind.Array)
            {
                return collectionElement.GetArrayLength();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse {Collection} collection while building capability summary", collectionPropertyName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error parsing {Collection} collection while building capability summary", collectionPropertyName);
        }

        return 0;
    }

    private bool TryParseInitializeResult(string? rawJson, out InitializeResult? initializeResult)
    {
        initializeResult = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("result", out var resultElement))
            {
                return false;
            }

            initializeResult = JsonSerializer.Deserialize<InitializeResult>(resultElement.GetRawText(), _jsonOptions);
            return initializeResult != null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse initialize response payload");
            return false;
        }
    }

    private string? TryGetFirstToolName(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("result", out var resultElement) ||
                !resultElement.TryGetProperty("tools", out var toolsElement) ||
                toolsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var toolElement in toolsElement.EnumerateArray())
            {
                if (toolElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool list response while extracting first tool name");
        }

        return null;
    }

    public async Task<ValidatorJsonRpcResponse> SendRawJsonAsync(string endpoint, string rawJson, CancellationToken cancellationToken, bool setContentType = true)
    {
        var (method, requestId) = TryReadProbeIdentity(rawJson);

        try
        {
            HttpContent content;
            if (setContentType)
            {
                content = CreateJsonContent(rawJson);
            }
            else
            {
                content = new StringContent(rawJson, Encoding.UTF8);
                content.Headers.ContentType = null; // Remove Content-Type header
            }

            using var requestMessage = CreatePostRequestMessage(
                endpoint,
                content,
                authentication: null,
                acceptWildcard: true,
                includeSessionId: ShouldIncludeSessionHeader(method));

            EnforceExecutionPolicy(endpoint);
            var httpResponse = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var responseJson = await ReadResponseContentAsync(httpResponse.Content, cancellationToken);
            var headers = BuildHeaderDictionary(httpResponse);

            CaptureHttpStateFromResponse(method, responseJson, headers);

            return new ValidatorJsonRpcResponse
            {
                StatusCode = (int)httpResponse.StatusCode,
                IsSuccess = httpResponse.IsSuccessStatusCode,
                RawJson = responseJson,
                Headers = headers,
                ProbeContext = CreateProbeContext(
                    method,
                    requestId,
                    _protocolVersion,
                    authApplied: false,
                    authScheme: null,
                    statusCode: (int)httpResponse.StatusCode,
                    isSuccess: httpResponse.IsSuccessStatusCode,
                    error: httpResponse.IsSuccessStatusCode ? null : $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}",
                    headers: headers)
            };
        }
        catch (Exception ex)
        {
            return new ValidatorJsonRpcResponse
            {
                StatusCode = -1,
                IsSuccess = false,
                Error = ex.Message,
                RawJson = null,
                ProbeContext = CreateProbeContext(
                    method,
                    requestId,
                    _protocolVersion,
                    authApplied: false,
                    authScheme: null,
                    statusCode: -1,
                    isSuccess: false,
                    error: ex.Message,
                    headers: null)
            };
        }
    }

    /// <summary>
    /// Sends a raw HTTP request to the specified endpoint.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
    {
        EnforceExecutionPolicy(endpoint);
        using var requestMessage = CreatePostRequestMessage(endpoint, content, authentication: null);
        return await _httpClient.SendAsync(requestMessage, cancellationToken);
    }

    public async Task<HttpTransportProbeResponse> SendHttpTransportProbeAsync(HttpTransportProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), request.Endpoint);

            if (request.Body is not null)
            {
                requestMessage.Content = CreateProbeContent(request.Body, request.ContentType);
            }

            foreach (var header in request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value) && requestMessage.Content is not null)
                {
                    requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (request.IncludeProtocolVersionHeader)
            {
                ApplyProtocolVersionHeader(requestMessage);
            }

            if (request.IncludeSessionIdHeader)
            {
                ApplySessionIdHeader(requestMessage, includeSessionId: true);
            }

            if (request.Authentication is not null)
            {
                var headerValue = McpAuthenticationHelper.BuildAuthorizationHeaderValue(request.Authentication);
                if (!string.IsNullOrEmpty(headerValue))
                {
                    McpAuthenticationHelper.ApplyAuthorizationHeader(requestMessage.Headers, headerValue);
                }
            }
            else if (request.IncludeDefaultAuthentication && _httpClient.DefaultRequestHeaders.Authorization is not null)
            {
                requestMessage.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
            }

            requestHeaders = BuildRequestHeaderDictionary(requestMessage);
            EnforceExecutionPolicy(request.Endpoint);

            var httpResponse = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var responseHeaders = BuildHeaderDictionary(httpResponse);
            var contentType = httpResponse.Content.Headers.ContentType?.MediaType;
            var body = IsEventStreamContentType(contentType)
                ? await ReadRawResponsePreviewAsync(httpResponse.Content, cancellationToken).ConfigureAwait(false)
                : await ReadRawResponseContentAsync(httpResponse.Content, cancellationToken).ConfigureAwait(false);
            var sseEvents = IsEventStreamContentType(contentType)
                ? SseEventStreamParser.Parse(body).ToList()
                : new List<SseEventRecord>();

            if (request.CaptureSessionId)
            {
                var (method, _) = TryReadProbeIdentity(request.Body ?? string.Empty);
                CaptureHttpStateFromResponse(method, body, responseHeaders);
            }

            return new HttpTransportProbeResponse
            {
                StatusCode = (int)httpResponse.StatusCode,
                IsSuccess = httpResponse.IsSuccessStatusCode,
                Body = body,
                ContentType = contentType,
                Headers = responseHeaders,
                RequestHeaders = requestHeaders,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                SseEvents = sseEvents,
                ProbeContext = CreateProbeContext(
                    null,
                    null,
                    _protocolVersion,
                    requestMessage.Headers.Authorization is not null,
                    requestMessage.Headers.Authorization?.Scheme,
                    (int)httpResponse.StatusCode,
                    httpResponse.IsSuccessStatusCode,
                    httpResponse.IsSuccessStatusCode ? null : $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}",
                    responseHeaders)
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "HTTP transport probe failed for {Endpoint}", request.Endpoint);
            return new HttpTransportProbeResponse
            {
                StatusCode = -1,
                IsSuccess = false,
                Error = ex.Message,
                RequestHeaders = requestHeaders,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                ProbeContext = CreateProbeContext(null, null, _protocolVersion, false, null, -1, false, ex.Message, null)
            };
        }
    }

    public Task<StdioTransportProbeResponse> SendStdioTransportProbeAsync(StdioTransportProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(new StdioTransportProbeResponse
        {
            ProbeId = request.ProbeId,
            Kind = request.Kind,
            StatusCode = -1,
            IsSuccess = false,
            Executed = false,
            Error = "Structured STDIO transport probes are not applicable to HTTP transports.",
            ProbeContext = CreateProbeContext(null, null, _protocolVersion, false, null, -1, false, "Structured STDIO transport probes are not applicable to HTTP transports.", null)
        });
    }

    private void EnforceExecutionPolicy(string endpoint)
    {
        if (_executionPolicy == null)
        {
            return;
        }

        var requestNumber = Interlocked.Increment(ref _requestCount);
        if (requestNumber > _executionPolicy.MaxRequests)
        {
            throw new InvalidOperationException($"Execution request budget exceeded ({_executionPolicy.MaxRequests}).");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (_executionPolicy.AllowedHosts.Count > 0 &&
            !_executionPolicy.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Execution policy blocked outbound request to host '{uri.Host}'.");
        }

        if (!_executionPolicy.AllowPrivateAddresses && IsPrivateAddress(uri.Host))
        {
            throw new InvalidOperationException($"Execution policy blocked private or loopback host '{uri.Host}'.");
        }
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

    private static ByteArrayContent CreateJsonContent(string jsonPayload)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(jsonPayload));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static HttpContent CreateProbeContent(string body, string? contentType)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return content;
    }

    private void ApplyProtocolVersionHeader(HttpRequestMessage requestMessage)
    {
        if (!string.IsNullOrEmpty(_protocolVersion) && !requestMessage.Headers.Contains(ProtocolVersionHeaderName))
        {
            requestMessage.Headers.TryAddWithoutValidation(ProtocolVersionHeaderName, _protocolVersion);
        }
    }

    private void ApplySessionIdHeader(HttpRequestMessage requestMessage, bool includeSessionId)
    {
        if (includeSessionId && !string.IsNullOrEmpty(_sessionId) && !requestMessage.Headers.Contains(SessionIdHeaderName))
        {
            requestMessage.Headers.TryAddWithoutValidation(SessionIdHeaderName, _sessionId);
        }
    }

    private static bool ShouldIncludeSessionHeader(string? method)
    {
        return !string.Equals(method, "initialize", StringComparison.Ordinal);
    }

    private void CaptureHttpStateFromResponse(string? method, string? body, IReadOnlyDictionary<string, string> headers)
    {
        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            if (TryReadInitializeProtocolVersion(body, out var negotiatedVersion))
            {
                SetProtocolVersion(negotiatedVersion);
            }

            if (headers.TryGetValue(SessionIdHeaderName, out var sessionId) && IsVisibleAscii(sessionId))
            {
                _sessionId = sessionId;
            }
        }
    }

    private static bool TryReadInitializeProtocolVersion(string? body, out string? protocolVersion)
    {
        protocolVersion = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("protocolVersion", out var versionElement) &&
                versionElement.ValueKind == JsonValueKind.String)
            {
                protocolVersion = versionElement.GetString();
                return !string.IsNullOrWhiteSpace(protocolVersion);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool IsVisibleAscii(string value)
    {
        return value.Length > 0 && value.All(static ch => ch is >= '!' and <= '~');
    }

    private static bool IsEventStreamContentType(string? mediaType)
    {
        return string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildRequestHeaderDictionary(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                headers[header.Key] = string.Join(",", header.Value);
            }
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }
        }

        return headers;
    }

    /// <summary>
    /// Tests basic connectivity to the server endpoint.
    /// </summary>
    public async Task<bool> TestConnectivityAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed; // GET may not be allowed, but server is reachable
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Performs a health check on the MCP server.
    /// </summary>
    public async Task<ValidationResult> HealthCheckAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            StartTime = DateTime.UtcNow,
            ServerConfig = new McpServerConfig { Endpoint = endpoint }
        };

        try
        {
            // Perform a detailed connectivity check
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                // Healthy - standard success or POST-only endpoint
                _logger.LogInformation("Health check passed: {StatusCode}", response.StatusCode);
            }
            else if (ValidationReliability.IsAuthenticationStatusCode((int)response.StatusCode))
            {
                // Healthy but protected - this confirms the server is reachable and responding
                _logger.LogInformation("Health check passed (Protected): {StatusCode}", response.StatusCode);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.OverallStatus = ValidationStatus.Failed;
                result.CriticalErrors.Add($"Endpoint not found (HTTP 404). Please check the URL: {endpoint}");
            }
            else if (ValidationReliability.IsTransientHealthStatusCode((int)response.StatusCode))
            {
                result.OverallStatus = ValidationStatus.Failed;
                result.CriticalErrors.Add($"Health check hit a transient server constraint ({(int)response.StatusCode} {response.StatusCode}). Retry later or continue with advisory validation if appropriate.");
            }
            else
            {
                result.OverallStatus = ValidationStatus.Failed;
                result.CriticalErrors.Add($"Server returned unhealthy status: {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            result.OverallStatus = ValidationStatus.Failed;
            result.CriticalErrors.Add($"Connection failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            result.OverallStatus = ValidationStatus.Failed;
            result.CriticalErrors.Add("Health check timed out after retry attempts");
        }
        catch (Exception ex)
        {
            result.OverallStatus = ValidationStatus.Failed;
            result.CriticalErrors.Add($"Health check error: {ex.Message}");
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task<string> ReadResponseContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var rawContent = await ReadRawResponseContentAsync(content, cancellationToken).ConfigureAwait(false);

        if (IsEventStreamContentType(content.Headers.ContentType?.MediaType) ||
            rawContent.TrimStart().StartsWith("event:", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSseResponse(rawContent);
        }

        return rawContent;
    }

    private async Task<string> ReadRawResponseContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try 
        {
            // Limit to 1MB to prevent memory issues/hanging on huge responses
            const long MaxResponseSize = 1 * 1024 * 1024; 
            
            if (content.Headers.ContentLength > MaxResponseSize)
            {
                _logger.LogWarning("Response too large ({Size} bytes). Max allowed is {Max} bytes.", content.Headers.ContentLength, MaxResponseSize);
                return string.Empty;
            }

            // If content length is unknown, we still need to be careful
            using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            
            var buffer = new char[4096];
            var memory = new Memory<char>(buffer);
            var sb = new StringBuilder();
            int totalRead = 0;
            int read;
            
            while ((read = await reader.ReadAsync(memory, cancellationToken)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxResponseSize)
                {
                    _logger.LogWarning("Response exceeded size limit of {Max} bytes. Truncating.", MaxResponseSize);
                    break;
                }
                sb.Append(buffer, 0, read);
            }
            
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading response content");
            return string.Empty;
        }
    }

    private async Task<string> ReadRawResponsePreviewAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            using var stream = await content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[4096];
            var builder = new StringBuilder();

            while (builder.Length < 32 * 1024)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 32 * 1024 - builder.Length)), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                builder.Append(buffer, 0, read);
                if (builder.ToString().Contains("\n\n", StringComparison.Ordinal) || builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return builder.ToString();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read SSE response preview");
            return string.Empty;
        }
    }

    private string ParseSseResponse(string sseContent)
    {
        var events = SseEventStreamParser.Parse(sseContent);
        return events.LastOrDefault(static evt => !string.IsNullOrWhiteSpace(evt.Data))?.Data ?? sseContent;
    }

    private bool CheckErrorCode(ValidatorJsonRpcResponse response, int expectedCode)
    {
        // Authentication challenges can legitimately gate protocol checks.
        if (expectedCode == -32700 && (response.StatusCode == 401 || response.StatusCode == 403))
        {
            _logger.LogDebug("Parse error correctly handled with HTTP {Status} (spec-compliant)", response.StatusCode);
            return true;
        }
        if (expectedCode == -32600 && (response.StatusCode == 401 || response.StatusCode == 403))
        {
            _logger.LogDebug("Invalid request correctly handled with HTTP {Status} (spec-compliant)", response.StatusCode);
            return true;
        }
        if (expectedCode == -32601 && (response.StatusCode == 401 || response.StatusCode == 403))
        {
            _logger.LogDebug("Method not found test properly protected by authentication (spec-compliant)");
            return true;
        }
        if (expectedCode == -32602 && (response.StatusCode == 401 || response.StatusCode == 403))
        {
            _logger.LogDebug("Invalid params test properly protected by authentication (spec-compliant)");
            return true;
        }

        // Everything else must surface a structured JSON-RPC error response.
        if (string.IsNullOrEmpty(response.RawJson)) return false;

        try
        {
            // Check if the response looks like JSON (starts with { or [)
            // This prevents trying to parse HTML error pages as JSON
            var trimmed = response.RawJson.Trim();
            if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            {
                _logger.LogWarning("Response does not appear to be JSON, received: {ResponseStart}",
                    trimmed.Substring(0, Math.Min(50, trimmed.Length)));
                return false;
            }

            var jsonDoc = JsonDocument.Parse(response.RawJson);
            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetInt32() == expectedCode;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response - server may be returning HTML error page. Response: {Response}",
                response.RawJson?.Substring(0, Math.Min(200, response.RawJson?.Length ?? 0)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing error code from response");
        }

        return false;
    }

    private static double CalculateCapabilityScore(
        bool capabilityDeclarationsAvailable,
        IReadOnlyCollection<string> advertisedCapabilities,
        bool toolListingSucceeded,
        bool toolInvocationSucceeded,
        bool resourceListingSucceeded,
        bool promptListingSucceeded)
    {
        var passed = 0;
        var totalChecks = 0;

        if (CapabilitySnapshotUtils.ShouldProbeCapability(capabilityDeclarationsAvailable, advertisedCapabilities, McpSpecConstants.Capabilities.Tools))
        {
            totalChecks++;
            if (toolListingSucceeded)
            {
                passed++;
            }

            totalChecks++;
            if (toolInvocationSucceeded)
            {
                passed++;
            }
        }

        if (CapabilitySnapshotUtils.ShouldProbeCapability(capabilityDeclarationsAvailable, advertisedCapabilities, McpSpecConstants.Capabilities.Resources))
        {
            totalChecks++;
            if (resourceListingSucceeded)
            {
                passed++;
            }
        }

        if (CapabilitySnapshotUtils.ShouldProbeCapability(capabilityDeclarationsAvailable, advertisedCapabilities, McpSpecConstants.Capabilities.Prompts))
        {
            totalChecks++;
            if (promptListingSucceeded)
            {
                passed++;
            }
        }

        if (totalChecks == 0)
        {
            return 100.0;
        }

        return passed / (double)totalChecks * 100;
    }

    private static bool IsAcceptableToolError(McpException ex)
    {
        var message = ex?.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.Contains("400", StringComparison.OrdinalIgnoreCase);
    }

    private static TransportMetadata CreateTransportMetadata(
        TimeSpan duration,
        int? statusCode = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? rawContent = null)
    {
        return new TransportMetadata
        {
            Duration = duration,
            StatusCode = statusCode,
            Headers = headers ?? TransportMetadata.Empty.Headers,
            RawContent = rawContent
        };
    }

    private McpServerConfig BuildServerConfig(string endpoint)
    {
        return new McpServerConfig
        {
            Endpoint = endpoint,
            ProtocolVersion = _protocolVersion,
            Authentication = CloneAuthentication(_defaultAuthentication),
            TimeoutMs = GetTimeoutMilliseconds(),
            Headers = ExtractDefaultHeaders()
        };
    }

    private int GetTimeoutMilliseconds()
    {
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan)
        {
            return 30000;
        }

        if (_httpClient.Timeout <= TimeSpan.Zero)
        {
            return 30000;
        }

        return (int)_httpClient.Timeout.TotalMilliseconds;
    }

    private Dictionary<string, string> ExtractDefaultHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return headers;
    }

    private static AuthenticationConfig? CloneAuthentication(AuthenticationConfig? authentication)
    {
        if (authentication == null)
        {
            return null;
        }

        return new AuthenticationConfig
        {
            Type = authentication.Type,
            Required = authentication.Required,
            Token = authentication.Token,
            Username = authentication.Username,
            Password = authentication.Password,
            ClientId = authentication.ClientId,
            TenantId = authentication.TenantId,
            Scopes = authentication.Scopes?.ToArray(),
            Authority = authentication.Authority,
            CustomHeaders = new Dictionary<string, string>(authentication.CustomHeaders ?? new Dictionary<string, string>()),
            AllowInteractive = authentication.AllowInteractive
        };
    }

    private async Task PopulateRecoveryProbeAsync(string endpoint, TransportResilienceProbeResult result, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var recoveryResponse = await CallAsync(endpoint, "rpc.recovery.probe", null, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        result.RecoveryElapsedMs = sw.Elapsed.TotalMilliseconds;
        result.RecoveryResponse = recoveryResponse;
        result.GracefulRecovery = IsResponsiveRecoveryResponse(recoveryResponse);

        if (!result.GracefulRecovery)
        {
            result.Notes.Add("Follow-up recovery probe did not receive a usable MCP response.");
        }
    }

    private bool IsResponsiveRecoveryResponse(ValidatorJsonRpcResponse response)
    {
        if (response.StatusCode <= 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(response.RawJson))
        {
            return true;
        }

        return response.IsSuccess || ValidationReliability.IsAuthenticationStatusCode(response.StatusCode);
    }

    private HttpRequestMessage CreatePostRequestMessage(
        string endpoint,
        HttpContent content,
        AuthenticationConfig? authentication,
        bool acceptWildcard = false,
        bool includeSessionId = true)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        if (acceptWildcard)
        {
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }
        else
        {
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        ApplyProtocolVersionHeader(requestMessage);
        ApplySessionIdHeader(requestMessage, includeSessionId);

        if (authentication != null)
        {
            var headerValue = McpAuthenticationHelper.BuildAuthorizationHeaderValue(authentication);
            if (!string.IsNullOrEmpty(headerValue))
            {
                McpAuthenticationHelper.ApplyAuthorizationHeader(requestMessage.Headers, headerValue);
            }
        }
        else if (_httpClient.DefaultRequestHeaders.Authorization != null)
        {
            requestMessage.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
        }

        return requestMessage;
    }

    private string CreateProbePayload(string method)
    {
        return JsonSerializer.Serialize(new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Id = Guid.NewGuid().ToString()
        }, _jsonOptions);
    }

    private static ValidatorJsonRpcResponse CreateSyntheticFailureResponse(string error, double elapsedMs)
    {
        return new ValidatorJsonRpcResponse
        {
            StatusCode = -1,
            IsSuccess = false,
            Error = error,
            ElapsedMs = elapsedMs,
            ProbeContext = CreateProbeContext(null, null, null, false, null, -1, false, error, null)
        };
    }

    private static Dictionary<string, string> BuildHeaderDictionary(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return headers;
    }

    private static (string? Method, string? RequestId) TryReadProbeIdentity(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
                ? methodElement.GetString()
                : null;
            var requestId = root.TryGetProperty("id", out var idElement)
                ? idElement.ValueKind switch
                {
                    JsonValueKind.String => idElement.GetString(),
                    JsonValueKind.Number => idElement.GetRawText(),
                    _ => null
                }
                : null;
            return (method, requestId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static ProbeContext CreateProbeContext(
        string? method,
        string? requestId,
        string? protocolVersion,
        bool authApplied,
        string? authScheme,
        int statusCode,
        bool isSuccess,
        string? error,
        IReadOnlyDictionary<string, string>? headers)
    {
        var responseClassification = ClassifyResponse(statusCode, isSuccess, error, headers);
        var authStatus = ClassifyAuthStatus(responseClassification, authApplied);
        var normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;

        return new ProbeContext
        {
            ProbeId = string.IsNullOrWhiteSpace(method) ? normalizedRequestId! : $"{method}:{normalizedRequestId}",
            RequestId = normalizedRequestId,
            Method = method,
            Transport = "http",
            ProtocolVersion = protocolVersion,
            AuthApplied = authApplied,
            AuthScheme = authScheme,
            AuthStatus = authStatus,
            ResponseClassification = responseClassification,
            Confidence = ClassifyConfidence(responseClassification),
            StatusCode = statusCode > 0 ? statusCode : null,
            Reason = error
        };
    }

    private static ProbeResponseClassification ClassifyResponse(int statusCode, bool isSuccess, string? error, IReadOnlyDictionary<string, string>? headers)
    {
        if (statusCode == (int)HttpStatusCode.Unauthorized)
        {
            return HasHeader(headers, "WWW-Authenticate")
                ? ProbeResponseClassification.AuthenticationChallenge
                : ProbeResponseClassification.AuthorizationFailure;
        }

        if (statusCode == (int)HttpStatusCode.Forbidden)
        {
            return ProbeResponseClassification.AuthorizationFailure;
        }

        if (statusCode == (int)HttpStatusCode.RequestTimeout || error?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ProbeResponseClassification.Timeout;
        }

        if (statusCode < 0)
        {
            return ProbeResponseClassification.TransportFailure;
        }

        if (ValidationReliability.IsRetryableHttpStatusCode(statusCode))
        {
            return ProbeResponseClassification.TransientFailure;
        }

        if (isSuccess)
        {
            return ProbeResponseClassification.Success;
        }

        return ProbeResponseClassification.ProtocolError;
    }

    private static ProbeAuthStatus ClassifyAuthStatus(ProbeResponseClassification responseClassification, bool authApplied)
    {
        return responseClassification switch
        {
            ProbeResponseClassification.AuthenticationChallenge => authApplied ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.AuthRequired,
            ProbeResponseClassification.AuthorizationFailure => authApplied ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.AuthRequired,
            ProbeResponseClassification.Success => authApplied ? ProbeAuthStatus.Applied : ProbeAuthStatus.NotRequired,
            _ => authApplied ? ProbeAuthStatus.Applied : ProbeAuthStatus.NotApplied
        };
    }

    private static EvidenceConfidenceLevel ClassifyConfidence(ProbeResponseClassification responseClassification)
    {
        return responseClassification switch
        {
            ProbeResponseClassification.Success or
                ProbeResponseClassification.AuthenticationChallenge or
                ProbeResponseClassification.AuthorizationFailure or
                ProbeResponseClassification.ProtocolError => EvidenceConfidenceLevel.High,
            ProbeResponseClassification.TransientFailure or
                ProbeResponseClassification.Timeout or
                ProbeResponseClassification.TransportFailure => EvidenceConfidenceLevel.Low,
            _ => EvidenceConfidenceLevel.None
        };
    }

    private static bool HasHeader(IReadOnlyDictionary<string, string>? headers, string headerName)
    {
        return headers?.ContainsKey(headerName) == true;
    }

    private sealed class DelayedAbortJsonContent : HttpContent
    {
        private readonly byte[] _firstChunk;
        private readonly byte[] _secondChunk;
        private readonly TimeSpan _delay;

        public DelayedAbortJsonContent(string payload, TimeSpan delay)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var splitIndex = Math.Max(1, bytes.Length / 2);
            _firstChunk = bytes[..splitIndex];
            _secondChunk = bytes[splitIndex..];
            _delay = delay;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _firstChunk.Length + _secondChunk.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamCoreAsync(stream, CancellationToken.None);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            return SerializeToStreamCoreAsync(stream, cancellationToken);
        }

        private async Task SerializeToStreamCoreAsync(Stream stream, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(_firstChunk, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await Task.Delay(_delay, cancellationToken);
            await stream.WriteAsync(_secondChunk, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }

    private sealed class InterruptingJsonContent : HttpContent
    {
        private readonly byte[] _firstChunk;

        public InterruptingJsonContent(string payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var splitIndex = Math.Max(1, bytes.Length / 2);
            _firstChunk = bytes[..splitIndex];
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamCoreAsync(stream, CancellationToken.None);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            return SerializeToStreamCoreAsync(stream, cancellationToken);
        }

        private async Task SerializeToStreamCoreAsync(Stream stream, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(_firstChunk, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            throw new IOException("Synthetic connection interruption injected by validator.");
        }
    }
}
