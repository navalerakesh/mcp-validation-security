using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
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
    private const int DefaultRequestTimeoutSeconds = 60;

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

    /// <summary>
    /// Performs a GET request to the specified URL and returns the response body as a string.
    /// </summary>
    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
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
        var request = new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = parameters,
            Id = Guid.NewGuid().ToString()
        };

        _logger.LogDebug("Making JSON-RPC call to {Endpoint}: {Method} with auth: {HasAuth}", endpoint, method, authentication != null);
        
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        
        return await ExecuteWithRetryAsync(endpoint, json, authentication, cancellationToken);
    }

    private async Task<ValidatorJsonRpcResponse> ExecuteWithRetryAsync(string endpoint, string jsonPayload, AuthenticationConfig? authentication, CancellationToken cancellationToken)
    {
        // Retry loop for 429 Too Many Requests
        int maxRetries = 3;
        int retryCount = 0;
        double? lastAttemptElapsedMs = null;

        var throttle = _requestSemaphore ?? throw new InvalidOperationException("HTTP concurrency limiter not initialized.");
        try
        {
            await throttle.WaitAsync(cancellationToken);

            while (true)
            {
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Create a new HttpRequestMessage to set authentication headers
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };

                // Add Accept header (Best Practice)
                requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

                // Advertise MCP protocol version if configured
                if (!string.IsNullOrEmpty(_protocolVersion))
                {
                    requestMessage.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
                }

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
                        _logger.LogDebug("Added authentication header");
                    }
                }
                else if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    // Fallback to global auth if set via SetAuthentication
                    requestMessage.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
                }
                else
                {
                    _logger.LogDebug("No authentication provided - making unauthenticated request");
                }

                // Create a fresh HttpClient to avoid any DI configuration issues
                // NOTE: In a real production scenario, we should use IHttpClientFactory.
                // However, for this specific method where we need to override auth per-request without affecting the global client,
                // creating a new client or using a named client is necessary.
                // Given the context of a validator tool, this is acceptable but could be optimized.
                using var freshHttpClient = new HttpClient();
                freshHttpClient.Timeout = TimeSpan.FromSeconds(DefaultRequestTimeoutSeconds);

                // Measure only the actual HTTP request/response cycle for
                // latency metrics. This intentionally excludes validator
                // backoff delays between retries and any external
                // scheduling overhead.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var httpResponse = await freshHttpClient.SendAsync(requestMessage, cancellationToken);
                sw.Stop();
                lastAttemptElapsedMs = sw.Elapsed.TotalMilliseconds;

                // Handle Rate Limiting (429)
                if ((int)httpResponse.StatusCode == 429 && retryCount < maxRetries)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(2 * retryCount); // Default backoff
                    
                    if (httpResponse.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        delay = httpResponse.Headers.RetryAfter.Delta.Value;
                    }
                    
                    _logger.LogWarning("Rate limited (429). Retrying in {Delay}s (Attempt {Retry}/{Max})...", delay.TotalSeconds, retryCount, maxRetries);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var responseJson = await ReadResponseContentAsync(httpResponse.Content, cancellationToken);

                _logger.LogDebug("Received response: {Status} - {Response}", httpResponse.StatusCode, 
                    responseJson?.Length > 1000 ? responseJson.Substring(0, 1000) + "..." : responseJson);

                // Capture all headers (both response headers and content headers)
                var allHeaders = new Dictionary<string, string>();
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

                return new ValidatorJsonRpcResponse
                {
                    StatusCode = (int)httpResponse.StatusCode,
                    IsSuccess = httpResponse.IsSuccessStatusCode,
                    RawJson = responseJson,
                    Headers = allHeaders,
                    Error = errorMessage,
                    ElapsedMs = lastAttemptElapsedMs
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
                ElapsedMs = null
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

    /// <summary>
    /// Tests the MCP initialize handshake.
    /// </summary>
    public async Task<TransportResult<InitializeResult>> ValidateInitializeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing MCP initialize handshake for {Endpoint}", endpoint);

        var startTime = DateTime.UtcNow;
        try
        {
            var initializeResponse = await CallAsync(endpoint, "initialize", new
            {
                protocolVersion = string.IsNullOrWhiteSpace(_protocolVersion) ? "2025-03-26" : _protocolVersion,
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

            if (!TryParseInitializeResult(initializeResponse.RawJson, out var initializeResult))
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
                Payload = initializeResult,
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
        IReadOnlyList<McpClientTool> discoveredTools = Array.Empty<McpClientTool>();
        var toolListingSucceeded = false;
        var toolInvocationSucceeded = false;
        string? firstToolName = null;

        var (toolListResponse, toolListDuration) = await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ToolsList, cancellationToken);
        var (resourceListResponse, resourceListDuration) = await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ResourcesList, cancellationToken);
        var (promptListResponse, promptListDuration) = await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.PromptsList, cancellationToken);

        if (toolListResponse?.IsSuccess == true)
        {
            toolListingSucceeded = true;
            firstToolName = TryGetFirstToolName(toolListResponse.RawJson);
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

        return new TransportResult<CapabilitySummary>
        {
            IsSuccessful = toolListingSucceeded,
            Error = toolListingSucceeded ? null : toolListResponse?.Error ?? "Tool listing failed",
            Payload = payload,
            Transport = CreateTransportMetadata(
                ResolveTransportDuration(),
                toolListResponse?.StatusCode,
                toolListResponse?.Headers,
                toolListResponse?.RawJson)
        };

        CapabilitySummary BuildSummary()
        {
            return new CapabilitySummary
            {
                Tools = discoveredTools,
                ToolListingSucceeded = toolListingSucceeded,
                ToolInvocationSucceeded = toolInvocationSucceeded,
                FirstToolName = firstToolName,
                DiscoveredToolsCount = discoveredTools.Count,
                Score = CalculateCapabilityScore(toolListingSucceeded, toolInvocationSucceeded),
                ToolListResponse = toolListResponse,
                ResourceListResponse = resourceListResponse,
                PromptListResponse = promptListResponse,
                ToolListDurationMs = toolListDuration,
                ResourceListDurationMs = resourceListDuration,
                PromptListDurationMs = promptListDuration,
                ResourceListingSucceeded = resourceListResponse?.IsSuccess == true,
                PromptListingSucceeded = promptListResponse?.IsSuccess == true,
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
        try
        {
            StringContent content;
            if (setContentType)
            {
                content = new StringContent(rawJson, Encoding.UTF8, "application/json");
            }
            else
            {
                content = new StringContent(rawJson, Encoding.UTF8);
                content.Headers.ContentType = null; // Remove Content-Type header
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            
            // Add Accept header (Best Practice)
            requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));

            // Advertise MCP protocol version if configured
            if (!string.IsNullOrEmpty(_protocolVersion))
            {
                requestMessage.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
            }

            // Explicitly apply authentication from default headers if present
            // This ensures auth works even if HttpClient behavior varies or if using a fresh client pattern elsewhere
            if (_httpClient.DefaultRequestHeaders.Authorization != null)
            {
                requestMessage.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
            }

            var httpResponse = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var responseJson = await ReadResponseContentAsync(httpResponse.Content, cancellationToken);

            return new ValidatorJsonRpcResponse
            {
                StatusCode = (int)httpResponse.StatusCode,
                IsSuccess = httpResponse.IsSuccessStatusCode,
                RawJson = responseJson,
                Headers = httpResponse.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))
            };
        }
        catch (Exception ex)
        {
            return new ValidatorJsonRpcResponse
            {
                StatusCode = -1,
                IsSuccess = false,
                Error = ex.Message,
                RawJson = null
            };
        }
    }

    /// <summary>
    /// Sends a raw HTTP request to the specified endpoint.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
    {
        return await _httpClient.PostAsync(endpoint, content, cancellationToken);
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
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Healthy but protected - this confirms the server is reachable and responding
                _logger.LogInformation("Health check passed (Protected): {StatusCode}", response.StatusCode);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.OverallStatus = ValidationStatus.Failed;
                result.CriticalErrors.Add($"Endpoint not found (HTTP 404). Please check the URL: {endpoint}");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                result.OverallStatus = ValidationStatus.Failed;
                result.CriticalErrors.Add($"Server is rate limiting requests (HTTP 429). Validation aborted to prevent abuse.");
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
            result.CriticalErrors.Add("Connection timed out");
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
            
            var rawContent = sb.ToString();

            // Check for SSE content type or content looking like SSE
            if (content.Headers.ContentType?.MediaType?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true ||
                rawContent.TrimStart().StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSseResponse(rawContent);
            }

            return rawContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading response content");
            return string.Empty;
        }
    }

    private string ParseSseResponse(string sseContent)
    {
        // Parse SSE format: look for the last "data:" line (handles multi-event streams)
        string? lastData = null;
        using var reader = new StringReader(sseContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                lastData = line.Substring(5).Trim();
            }
            // SSE keep-alive: empty lines or comments (":" prefix) are ignored
        }
        return lastData ?? sseContent;
    }

    private bool CheckErrorCode(ValidatorJsonRpcResponse response, int expectedCode)
    {
        // JSON-RPC 2.0 spec allows both HTTP-level and JSON-RPC-level error handling
        // Check HTTP status codes first (transport-level rejection)
        if (expectedCode == -32700 && (response.StatusCode == 400 || response.StatusCode == 401 || response.StatusCode == 403)) // Parse error
        {
            _logger.LogDebug("Parse error correctly handled with HTTP {Status} (spec-compliant)", response.StatusCode);
            return true;
        }
        if (expectedCode == -32600 && (response.StatusCode == 400 || response.StatusCode == 401 || response.StatusCode == 403)) // Invalid request
        {
            _logger.LogDebug("Invalid request correctly handled with HTTP {Status} (spec-compliant)", response.StatusCode);
            return true;
        }
        if (expectedCode == -32601 && (response.StatusCode == 401 || response.StatusCode == 403)) // Method not found -> auth error acceptable
        {
            _logger.LogDebug("Method not found test properly protected by authentication (spec-compliant)");
            return true;
        }
        if (expectedCode == -32602 && (response.StatusCode == 401 || response.StatusCode == 403)) // Invalid params -> auth error acceptable
        {
            _logger.LogDebug("Invalid params test properly protected by authentication (spec-compliant)");
            return true;
        }

        // Check JSON-RPC error codes (application-level error handling)
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

    private static double CalculateCapabilityScore(bool toolListingSucceeded, bool toolInvocationSucceeded)
    {
        var passed = 0;
        if (toolListingSucceeded)
        {
            passed++;
        }

        if (toolInvocationSucceeded)
        {
            passed++;
        }

        const int totalChecks = 2;
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
}
