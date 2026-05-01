using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Utilities;
using Mcp.Compliance.Spec;
using ModelContextProtocol.Protocol;
using ValidatorJsonRpcRequest = Mcp.Benchmark.Core.Models.JsonRpcRequest;
using ValidatorJsonRpcResponse = Mcp.Benchmark.Core.Models.JsonRpcResponse;

namespace Mcp.Benchmark.Infrastructure.Http;

/// <summary>
/// MCP client adapter for STDIO transport. Spawns the MCP server as a child process
/// and communicates via stdin/stdout using JSON-RPC 2.0 newline-delimited messages.
/// Implements IMcpHttpClient so all validators can run transparently against local servers.
/// </summary>
public class StdioMcpClientAdapter : IMcpHttpClient, IDisposable, IAsyncDisposable
{
    private readonly ILogger<StdioMcpClientAdapter> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _stderrLock = new();
    private readonly List<string> _stderrLines = new();
    private Process? _serverProcess;
    private string? _startupCommand;
    private Dictionary<string, string>? _startupEnvironment;
    private ExecutionPolicy? _executionPolicy;
    private int _requestCount;
    private bool _initializeSucceeded;
    private bool _initializedNotificationSent;
    private bool _disposed;
    private const int MaxCapturedStderrLines = 50;

    public StdioMcpClientAdapter(ILogger<StdioMcpClientAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task StartProcessAsync(string command, Dictionary<string, string>? environment = null, CancellationToken ct = default)
    {
        _startupCommand = command;
        _startupEnvironment = environment == null
            ? null
            : new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);

        await StartProcessCoreAsync(command, environment, ct);
    }

    private async Task StartProcessCoreAsync(string command, Dictionary<string, string>? environment, CancellationToken ct)
    {
        var parts = ParseCommand(command);
        if (parts.Length == 0)
        {
            throw new ArgumentException("STDIO endpoint must specify a command to execute.", nameof(command));
        }

        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        for (var index = 1; index < parts.Length; index++)
        {
            psi.ArgumentList.Add(parts[index]);
        }

        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        _logger.LogInformation("Starting STDIO MCP server: {Command}", command);
        ClearCapturedStderr();
        _serverProcess = Process.Start(psi);

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            throw new InvalidOperationException($"Failed to start STDIO server process: {command}");
        }

        StartStderrCapture(_serverProcess);

        await Task.Delay(500, ct);

        if (_serverProcess.HasExited)
        {
            var stderr = await _serverProcess.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"STDIO server exited immediately. stderr: {stderr}");
        }

        _logger.LogInformation("STDIO server process started (PID: {Pid})", _serverProcess.Id);
        ResetSessionState();
    }

    public Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return CallAsync(endpoint, method, parameters, null, cancellationToken);
    }

    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken = default)
    {
        EnforceExecutionPolicy();
        var isInitializeRequest = string.Equals(method, McpSpecConstants.InitializeMethod, StringComparison.Ordinal);
        var requestId = Guid.NewGuid().ToString();

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 503,
                IsSuccess = false,
                Error = "STDIO server process is not running.",
                ProbeContext = CreateProbeContext(method, requestId, 503, false, "STDIO server process is not running.")
            };
        }

        var request = new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = parameters,
            Id = requestId
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!isInitializeRequest && _initializeSucceeded && !_initializedNotificationSent)
            {
                await SendInitializedNotificationCoreAsync(cancellationToken);
            }

            var sw = Stopwatch.StartNew();
            await _serverProcess.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var responseLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromSeconds(30), cancellationToken);
            sw.Stop();

            if (string.IsNullOrEmpty(responseLine))
            {
                if (isInitializeRequest)
                {
                    ResetSessionState();
                }

                return new ValidatorJsonRpcResponse
                {
                    StatusCode = 500,
                    IsSuccess = false,
                    Error = "Empty response from STDIO server",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    ProbeContext = CreateProbeContext(method, requestId, 500, false, "Empty response from STDIO server")
                };
            }

            using var doc = JsonDocument.Parse(responseLine);
            var hasError = doc.RootElement.TryGetProperty("error", out _);

            if (isInitializeRequest)
            {
                _initializeSucceeded = !hasError;
                _initializedNotificationSent = false;
            }

            return new ValidatorJsonRpcResponse
            {
                StatusCode = hasError ? 400 : 200,
                IsSuccess = !hasError,
                RawJson = responseLine,
                Error = hasError ? doc.RootElement.GetProperty("error").GetProperty("message").GetString() : null,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                ProbeContext = CreateProbeContext(
                    method,
                    requestId,
                    hasError ? 400 : 200,
                    !hasError,
                    hasError ? doc.RootElement.GetProperty("error").GetProperty("message").GetString() : null)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (isInitializeRequest)
            {
                ResetSessionState();
            }

            _logger.LogError(ex, "STDIO call to {Method} failed", method);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                Error = $"STDIO transport error: {ex.Message}",
                ProbeContext = CreateProbeContext(method, requestId, 500, false, $"STDIO transport error: {ex.Message}")
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<HttpResponseMessage> SendAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotImplemented)
        {
            Content = new StringContent("SendAsync is not supported on STDIO transport.")
        });
    }

    public Task<bool> TestConnectivityAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_serverProcess != null && !_serverProcess.HasExited);
    }

    public async Task<ValidationResult> HealthCheckAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();
        var response = await CallAsync(endpoint, "ping", null, cancellationToken);
        result.OverallStatus = response.IsSuccess ? ValidationStatus.Passed : ValidationStatus.Failed;
        return result;
    }

    public async Task<JsonRpcErrorValidationResult> ValidateErrorCodesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var results = new List<JsonRpcErrorTest>();

        var parseErrorResponse = await SendRawJsonAsync(endpoint, "{ invalid json", cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Parse Error",
            Payload = "{ invalid json",
            ExpectedErrorCode = -32700,
            ActualResponse = parseErrorResponse,
            IsValid = CheckErrorCode(parseErrorResponse, -32700)
        });

        var invalidRequestResponse = await SendRawJsonAsync(endpoint,
            "{\"method\":\"test\",\"id\":1,\"params\":{}}",
            cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Invalid Request - Missing jsonrpc",
            Payload = "{\"method\":\"test\",\"id\":1,\"params\":{}}",
            ExpectedErrorCode = -32600,
            ActualResponse = invalidRequestResponse,
            IsValid = CheckErrorCode(invalidRequestResponse, -32600)
        });

        var methodNotFoundResponse = await CallAsync(endpoint, "rpc.invalid.method", null, cancellationToken);
        results.Add(new JsonRpcErrorTest
        {
            Name = "Invalid Method",
            ExpectedErrorCode = -32601,
            ActualResponse = methodNotFoundResponse,
            IsValid = CheckErrorCode(methodNotFoundResponse, -32601)
        });

        var invalidParamsResponse = await CallAsync(endpoint, ValidationConstants.Methods.ToolsCall, "invalid_params_string", cancellationToken);
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
            OverallScore = results.Count(test => test.IsValid) / (double)results.Count * 100,
            IsCompliant = results.All(test => test.IsValid)
        };
    }

    public async Task<TransportResilienceProbeResult> ProbeTimeoutRecoveryAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = new TransportResilienceProbeResult
        {
            ProbeId = "timeout-handling",
            DisplayName = "Timeout Handling Recovery",
            ExpectedOutcome = "A validator-induced STDIO read timeout should be observed and a follow-up MCP request should still succeed."
        };

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            result.ActualOutcome = "STDIO server process is not running.";
            return result;
        }

        var rawRequest = JsonSerializer.Serialize(new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = "rpc.timeout.probe",
            Id = Guid.NewGuid().ToString()
        }, _jsonOptions);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            result.Executed = true;
            var sw = Stopwatch.StartNew();
            await _serverProcess.StandardInput.WriteLineAsync(rawRequest.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var timedOutRead = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.Zero, cancellationToken);
            sw.Stop();

            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            if (string.IsNullOrEmpty(timedOutRead))
            {
                result.FailureObserved = true;
                result.FailureResponse = CreateSyntheticFailureResponse("STDIO response timed out during validator-induced timeout probe.", result.FailureElapsedMs);
                result.ActualOutcome = "Validator induced a zero-window STDIO read timeout and then drained the pending response.";
                _ = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromSeconds(2), cancellationToken);
            }
            else
            {
                result.FailureResponse = CreateResponseFromLine(timedOutRead, result.FailureElapsedMs);
                result.ActualOutcome = "Server responded before the induced STDIO timeout window elapsed.";
            }
        }
        finally
        {
            _lock.Release();
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
            ExpectedOutcome = "A validator-induced STDIO process interruption should be observed and the child process should restart cleanly for a follow-up MCP request."
        };

        if (string.IsNullOrWhiteSpace(_startupCommand))
        {
            result.ActualOutcome = "STDIO restart probe is unavailable because the adapter was not started with a restartable command.";
            return result;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            result.Executed = true;
            var sw = Stopwatch.StartNew();
            StopServerProcess();
            sw.Stop();

            result.FailureObserved = true;
            result.FailureElapsedMs = sw.Elapsed.TotalMilliseconds;
            result.FailureResponse = CreateSyntheticFailureResponse("STDIO server process terminated by validator to simulate a transport interruption.", result.FailureElapsedMs);
            result.ActualOutcome = "Validator terminated the STDIO child process and restarted it for recovery verification.";

            await StartProcessCoreAsync(_startupCommand, _startupEnvironment, cancellationToken);
        }
        catch (Exception ex)
        {
            result.ActualOutcome = $"STDIO interruption probe failed before recovery verification: {ex.Message}";
            result.Notes.Add(ex.Message);
            return result;
        }
        finally
        {
            _lock.Release();
        }

        await PopulateRecoveryProbeAsync(endpoint, result, cancellationToken);
        return result;
    }

    public async Task<TransportResult<InitializeResult>> ValidateInitializeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var response = await CallAsync(endpoint, McpSpecConstants.InitializeMethod, CreateInitializeParameters(), cancellationToken);
        sw.Stop();

        var transportResult = new TransportResult<InitializeResult>
        {
            Transport = new TransportMetadata { Duration = sw.Elapsed },
            IsSuccessful = response.IsSuccess
        };

        if (response.IsSuccess && !string.IsNullOrEmpty(response.RawJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(response.RawJson);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var payload = JsonSerializer.Deserialize<InitializeResult>(result.GetRawText(), _jsonOptions);
                    return new TransportResult<InitializeResult>
                    {
                        Transport = new TransportMetadata { Duration = sw.Elapsed },
                        IsSuccessful = true,
                        Payload = payload
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse initialize response");
            }
        }

        return transportResult;
    }

    public async Task<TransportResult<CapabilitySummary>> ValidateCapabilitiesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var initResult = await ValidateInitializeAsync(endpoint, cancellationToken);

        if (!initResult.IsSuccessful)
        {
            return new TransportResult<CapabilitySummary>
            {
                Transport = initResult.Transport,
                IsSuccessful = false,
                Error = initResult.Error ?? "Initialize failed"
            };
        }

        var capabilityDeclarationsAvailable = CapabilitySnapshotUtils.HasCapabilityDeclarations(initResult.Payload);
        var advertisedCapabilities = CapabilitySnapshotUtils.ExtractAdvertisedCapabilities(initResult.Payload);
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

        var (toolListResponse, toolListDuration) = shouldProbeTools
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ToolsList, cancellationToken)
            : (null, 0);
        var (resourceListResponse, resourceListDuration) = shouldProbeResources
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.ResourcesList, cancellationToken)
            : (null, 0);
        var (promptListResponse, promptListDuration) = shouldProbePrompts
            ? await ProbeJsonRpcAsync(endpoint, ValidationConstants.Methods.PromptsList, cancellationToken)
            : (null, 0);

        var toolListingSucceeded = toolListResponse?.IsSuccess == true;
        var firstToolName = toolListingSucceeded ? TryGetFirstToolName(toolListResponse?.RawJson) : null;
        var toolInvocationSucceeded = false;
        var resourceListingSucceeded = resourceListResponse?.IsSuccess == true;
        var promptListingSucceeded = promptListResponse?.IsSuccess == true;

        if (toolListingSucceeded && !string.IsNullOrWhiteSpace(firstToolName))
        {
            try
            {
                var toolCallResponse = await CallAsync(endpoint, ValidationConstants.Methods.ToolsCall, new
                {
                    name = firstToolName,
                    arguments = new { }
                }, cancellationToken);

                toolInvocationSucceeded = toolCallResponse.IsSuccess || toolCallResponse.StatusCode == 400;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "STDIO capability snapshot tool invocation failed for {Tool}", firstToolName);
            }
        }

        return new TransportResult<CapabilitySummary>
        {
            Transport = new TransportMetadata
            {
                Duration = toolListDuration > 0 ? TimeSpan.FromMilliseconds(toolListDuration) : initResult.Transport.Duration,
                StatusCode = toolListResponse?.StatusCode ?? initResult.Transport.StatusCode,
                Headers = toolListResponse?.Headers ?? initResult.Transport.Headers,
                RawContent = toolListResponse?.RawJson ?? initResult.Transport.RawContent
            },
            IsSuccessful = (initResult.IsSuccessful && capabilityDeclarationsAvailable) || toolListingSucceeded || resourceListingSucceeded || promptListingSucceeded,
            Error = (initResult.IsSuccessful && capabilityDeclarationsAvailable) || toolListingSucceeded || resourceListingSucceeded || promptListingSucceeded
                ? null
                : toolListResponse?.Error ?? "Capability discovery failed",
            Payload = new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = capabilityDeclarationsAvailable,
                AdvertisedCapabilities = advertisedCapabilities,
                ToolListingSucceeded = toolListingSucceeded,
                ToolInvocationSucceeded = toolInvocationSucceeded,
                FirstToolName = firstToolName,
                DiscoveredToolsCount = CountCollectionItems(toolListResponse, "tools"),
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
            }
        };
    }

    public async Task<ValidatorJsonRpcResponse> SendRawJsonAsync(string endpoint, string rawJson, CancellationToken cancellationToken, bool setContentType = true)
    {
        EnforceExecutionPolicy();

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            var (method, requestId) = TryReadProbeIdentity(rawJson);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 503,
                IsSuccess = false,
                Error = "STDIO server not running.",
                ProbeContext = CreateProbeContext(method, requestId, 503, false, "STDIO server not running.")
            };
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _serverProcess.StandardInput.WriteLineAsync(rawJson.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var responseLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromSeconds(10), cancellationToken);
            var (method, requestId) = TryReadProbeIdentity(rawJson);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = string.IsNullOrEmpty(responseLine) ? 500 : 200,
                IsSuccess = !string.IsNullOrEmpty(responseLine),
                RawJson = responseLine,
                Error = string.IsNullOrEmpty(responseLine) ? "No response" : null,
                ProbeContext = CreateProbeContext(method, requestId, string.IsNullOrEmpty(responseLine) ? 500 : 200, !string.IsNullOrEmpty(responseLine), string.IsNullOrEmpty(responseLine) ? "No response" : null)
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public void SetAuthentication(AuthenticationConfig? authentication) { }
    public void SetConcurrencyLimit(int maxConcurrency) { }
    public void SetProtocolVersion(string? protocolVersion) { }
    public void ConfigureExecutionPolicy(ExecutionPolicy? executionPolicy)
    {
        _executionPolicy = executionPolicy?.Clone();
        Interlocked.Exchange(ref _requestCount, 0);
    }

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    public Task<HttpTransportProbeResponse> SendHttpTransportProbeAsync(HttpTransportProbeRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HttpTransportProbeResponse
        {
            StatusCode = -1,
            IsSuccess = false,
            Error = "Structured HTTP transport probes are not applicable to stdio transports."
        });
    }

    public async Task<StdioTransportProbeResponse> SendStdioTransportProbeAsync(StdioTransportProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnforceExecutionPolicy();

        return request.Kind switch
        {
            StdioTransportProbeKind.MessageExchange => await ExecuteMessageExchangeProbeAsync(request, cancellationToken),
            StdioTransportProbeKind.ShutdownLifecycle => await ExecuteShutdownLifecycleProbeAsync(request, cancellationToken),
            _ => CreateUnavailableStdioProbeResponse(request, "Unsupported STDIO transport probe kind.")
        };
    }

    private async Task<StdioTransportProbeResponse> ExecuteMessageExchangeProbeAsync(StdioTransportProbeRequest request, CancellationToken cancellationToken)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            return CreateUnavailableStdioProbeResponse(request, "STDIO server not running.");
        }

        if (string.IsNullOrWhiteSpace(request.RawMessage))
        {
            return CreateUnavailableStdioProbeResponse(request, "STDIO message exchange probe requires a raw message.");
        }

        var (method, requestId) = TryReadProbeIdentity(request.RawMessage);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1, request.ResponseTimeoutMs));

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            await _serverProcess.StandardInput.WriteLineAsync(request.RawMessage.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var responseLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, timeout, cancellationToken);
            string? extraStdoutLine = null;
            if (!string.IsNullOrEmpty(responseLine))
            {
                extraStdoutLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromMilliseconds(25), cancellationToken);
            }

            sw.Stop();
            var hasResponse = !string.IsNullOrEmpty(responseLine);
            var error = hasResponse ? null : "No response from STDIO stdout.";
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(extraStdoutLine))
            {
                metadata["extraStdoutLine"] = extraStdoutLine!;
            }

            return new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = hasResponse ? 200 : 500,
                IsSuccess = hasResponse,
                Executed = true,
                RawStdout = responseLine,
                StderrPreview = SnapshotStderr(),
                Error = error,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                ProbeContext = CreateProbeContext(method, requestId, hasResponse ? 200 : 500, hasResponse, error),
                Metadata = metadata
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StdioTransportProbeResponse> ExecuteShutdownLifecycleProbeAsync(StdioTransportProbeRequest request, CancellationToken cancellationToken)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            return CreateUnavailableStdioProbeResponse(request, "STDIO server not running.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            var shutdownMode = "stdin-close";
            var processExited = false;
            var restarted = false;
            string? error = null;

            try
            {
                _serverProcess.StandardInput.Close();
                processExited = _serverProcess.WaitForExit(1000);
                if (!processExited && !_serverProcess.HasExited)
                {
                    shutdownMode = "kill";
                    _serverProcess.Kill(entireProcessTree: true);
                    processExited = _serverProcess.WaitForExit(3000);
                }

                processExited = processExited || _serverProcess.HasExited;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
                ResetSessionState();
            }

            if (processExited && !string.IsNullOrWhiteSpace(_startupCommand))
            {
                try
                {
                    await StartProcessCoreAsync(_startupCommand, _startupEnvironment, cancellationToken);
                    restarted = _serverProcess != null && !_serverProcess.HasExited;
                }
                catch (Exception ex)
                {
                    error = string.IsNullOrWhiteSpace(error) ? ex.Message : $"{error}; restart failed: {ex.Message}";
                }
            }

            sw.Stop();
            var success = processExited && (string.IsNullOrWhiteSpace(_startupCommand) || restarted);

            return new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = success ? 200 : 500,
                IsSuccess = success,
                Executed = true,
                StderrPreview = SnapshotStderr(),
                Error = error,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                ProcessExited = processExited,
                Restarted = string.IsNullOrWhiteSpace(_startupCommand) ? null : restarted,
                ShutdownMode = shutdownMode,
                ProbeContext = CreateProbeContext("stdio/shutdown", request.ProbeId, success ? 200 : 500, success, error)
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool CheckErrorCode(ValidatorJsonRpcResponse response, int expectedCode)
    {
        if (string.IsNullOrWhiteSpace(response.RawJson))
        {
            return false;
        }

        try
        {
            var trimmed = response.RawJson.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return false;
            }

            using var json = JsonDocument.Parse(response.RawJson);
            return json.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("code", out var code) &&
                   code.GetInt32() == expectedCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void EnforceExecutionPolicy()
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
    }

    private async Task<(ValidatorJsonRpcResponse? Response, double Duration)> ProbeJsonRpcAsync(string endpoint, string method, CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var response = await CallAsync(endpoint, method, null, cancellationToken);
            sw.Stop();
            return (response, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "STDIO capability probe {Method} failed", method);
            return (null, 0);
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
            _logger.LogDebug(ex, "Failed to parse {Collection} collection while building STDIO capability summary", collectionPropertyName);
        }

        return 0;
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
            _logger.LogDebug(ex, "Failed to parse tool list response while extracting the first tool name");
        }

        return null;
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

    private static object CreateInitializeParameters() => new
    {
        protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(null),
        capabilities = new { },
        clientInfo = new { name = "mcp-benchmark", version = "1.0.0" }
    };

    private async Task SendInitializedNotificationCoreAsync(CancellationToken cancellationToken)
    {
        if (_serverProcess == null || _serverProcess.HasExited || !_initializeSucceeded || _initializedNotificationSent)
        {
            return;
        }

        var notificationJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = McpSpecConstants.InitializedNotification
        }, _jsonOptions);

        await _serverProcess.StandardInput.WriteLineAsync(notificationJson.AsMemory(), cancellationToken);
        await _serverProcess.StandardInput.FlushAsync();

        var unexpectedResponse = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromMilliseconds(100), cancellationToken);
        if (!string.IsNullOrWhiteSpace(unexpectedResponse))
        {
            _logger.LogWarning("STDIO server responded to {Notification}: {Response}", McpSpecConstants.InitializedNotification, unexpectedResponse);
        }

        _initializedNotificationSent = true;
    }

    private void ResetSessionState()
    {
        _initializeSucceeded = false;
        _initializedNotificationSent = false;
    }

    private async Task PopulateRecoveryProbeAsync(string endpoint, TransportResilienceProbeResult result, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var recoveryResponse = await CallAsync(endpoint, "rpc.recovery.probe", null, cancellationToken);
        sw.Stop();

        result.RecoveryElapsedMs = sw.Elapsed.TotalMilliseconds;
        result.RecoveryResponse = recoveryResponse;
        result.GracefulRecovery = IsResponsiveRecoveryResponse(recoveryResponse);

        if (!result.GracefulRecovery)
        {
            result.Notes.Add("Follow-up recovery probe did not produce a usable STDIO response.");
        }
    }

    private static bool IsResponsiveRecoveryResponse(ValidatorJsonRpcResponse response)
    {
        if (response.StatusCode <= 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(response.RawJson))
        {
            return true;
        }

        return response.IsSuccess;
    }

    private static ValidatorJsonRpcResponse CreateSyntheticFailureResponse(string error, double elapsedMs)
    {
        return new ValidatorJsonRpcResponse
        {
            StatusCode = -1,
            IsSuccess = false,
            Error = error,
            ElapsedMs = elapsedMs,
            ProbeContext = CreateProbeContext(null, null, -1, false, error)
        };
    }

    private static ValidatorJsonRpcResponse CreateResponseFromLine(string responseLine, double elapsedMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseLine);
            var hasError = doc.RootElement.TryGetProperty("error", out var errorElement);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = hasError ? 400 : 200,
                IsSuccess = !hasError,
                RawJson = responseLine,
                Error = hasError && errorElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null,
                ElapsedMs = elapsedMs,
                ProbeContext = CreateProbeContext(null, null, hasError ? 400 : 200, !hasError, hasError && errorElement.TryGetProperty("message", out var contextMessageElement) ? contextMessageElement.GetString() : null)
            };
        }
        catch (JsonException)
        {
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = responseLine,
                ElapsedMs = elapsedMs,
                ProbeContext = CreateProbeContext(null, null, 200, true, null)
            };
        }
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

    private static ProbeContext CreateProbeContext(string? method, string? requestId, int statusCode, bool isSuccess, string? error)
    {
        var normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        var responseClassification = ClassifyStdioResponse(statusCode, isSuccess, error);

        return new ProbeContext
        {
            ProbeId = string.IsNullOrWhiteSpace(method) ? normalizedRequestId! : $"{method}:{normalizedRequestId}",
            RequestId = normalizedRequestId,
            Method = method,
            Transport = "stdio",
            AuthApplied = false,
            AuthStatus = ProbeAuthStatus.NotApplied,
            ResponseClassification = responseClassification,
            Confidence = responseClassification == ProbeResponseClassification.Success || responseClassification == ProbeResponseClassification.ProtocolError
                ? EvidenceConfidenceLevel.High
                : EvidenceConfidenceLevel.Low,
            StatusCode = statusCode > 0 ? statusCode : null,
            Reason = error
        };
    }

    private static ProbeResponseClassification ClassifyStdioResponse(int statusCode, bool isSuccess, string? error)
    {
        if (isSuccess)
        {
            return ProbeResponseClassification.Success;
        }

        if (statusCode < 0)
        {
            return ProbeResponseClassification.TransportFailure;
        }

        if (error?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ProbeResponseClassification.Timeout;
        }

        if (error?.Contains("No response", StringComparison.OrdinalIgnoreCase) == true ||
            error?.Contains("Empty response", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ProbeResponseClassification.NoResponse;
        }

        return ProbeResponseClassification.ProtocolError;
    }

    private void StartStderrCapture(Process process)
    {
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrEmpty(eventArgs.Data))
            {
                return;
            }

            lock (_stderrLock)
            {
                _stderrLines.Add(eventArgs.Data);
                if (_stderrLines.Count > MaxCapturedStderrLines)
                {
                    _stderrLines.RemoveRange(0, _stderrLines.Count - MaxCapturedStderrLines);
                }
            }
        };

        try
        {
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "STDIO stderr capture could not be started for process {ProcessId}", process.Id);
        }
    }

    private string? SnapshotStderr()
    {
        lock (_stderrLock)
        {
            return _stderrLines.Count == 0 ? null : string.Join(Environment.NewLine, _stderrLines);
        }
    }

    private void ClearCapturedStderr()
    {
        lock (_stderrLock)
        {
            _stderrLines.Clear();
        }
    }

    private static StdioTransportProbeResponse CreateUnavailableStdioProbeResponse(StdioTransportProbeRequest request, string error)
    {
        return new StdioTransportProbeResponse
        {
            ProbeId = request.ProbeId,
            Kind = request.Kind,
            StatusCode = -1,
            IsSuccess = false,
            Executed = false,
            Error = error,
            ProbeContext = CreateProbeContext(null, request.ProbeId, -1, false, error)
        };
    }

    private void StopServerProcess()
    {
        if (_serverProcess == null)
        {
            return;
        }

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(3000);
            }
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
            ResetSessionState();
        }
    }

    private static string[] ParseCommand(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;

        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (ch == ' ' && !inQuote && current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        if (parts.Count > 0 && !Path.HasExtension(parts[0]) && !Path.IsPathRooted(parts[0]))
        {
            var resolved = ResolveExecutable(parts[0]);
            if (resolved != null)
            {
                parts[0] = resolved;
            }
        }

        return parts.ToArray();
    }

    private static string? ResolveExecutable(string name)
    {
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".cmd", ".exe", ".bat", string.Empty }
            : new[] { string.Empty, ".sh" };

        foreach (var dir in pathDirs)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, name + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.StandardInput.Close();
            }
            catch
            {
            }
        }

        StopServerProcess();
        _lock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
