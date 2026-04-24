using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
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
    private Process? _serverProcess;
    private string? _startupCommand;
    private Dictionary<string, string>? _startupEnvironment;
    private bool _disposed;

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
        _serverProcess = Process.Start(psi);

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            throw new InvalidOperationException($"Failed to start STDIO server process: {command}");
        }

        await Task.Delay(500, ct);

        if (_serverProcess.HasExited)
        {
            var stderr = await _serverProcess.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"STDIO server exited immediately. stderr: {stderr}");
        }

        _logger.LogInformation("STDIO server process started (PID: {Pid})", _serverProcess.Id);
    }

    public Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return CallAsync(endpoint, method, parameters, null, cancellationToken);
    }

    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken = default)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            return new ValidatorJsonRpcResponse { StatusCode = 503, IsSuccess = false, Error = "STDIO server process is not running." };
        }

        var request = new ValidatorJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = parameters,
            Id = Guid.NewGuid().ToString()
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            await _serverProcess.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var responseLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromSeconds(30), cancellationToken);
            sw.Stop();

            if (string.IsNullOrEmpty(responseLine))
            {
                return new ValidatorJsonRpcResponse
                {
                    StatusCode = 500,
                    IsSuccess = false,
                    Error = "Empty response from STDIO server",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            using var doc = JsonDocument.Parse(responseLine);
            var hasError = doc.RootElement.TryGetProperty("error", out _);

            return new ValidatorJsonRpcResponse
            {
                StatusCode = hasError ? 400 : 200,
                IsSuccess = !hasError,
                RawJson = responseLine,
                Error = hasError ? doc.RootElement.GetProperty("error").GetProperty("message").GetString() : null,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "STDIO call to {Method} failed", method);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                Error = $"STDIO transport error: {ex.Message}"
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
        var response = await CallAsync(endpoint, "rpc.invalid.method", null, cancellationToken);
        return new JsonRpcErrorValidationResult
        {
            Tests =
            [
                new JsonRpcErrorTest
                {
                    Name = "Invalid Method",
                    ExpectedErrorCode = -32601,
                    ActualResponse = response,
                    IsValid = !response.IsSuccess || (response.RawJson?.Contains("-32601", StringComparison.OrdinalIgnoreCase) == true)
                }
            ],
            OverallScore = !response.IsSuccess || (response.RawJson?.Contains("-32601", StringComparison.OrdinalIgnoreCase) == true) ? 100 : 0,
            IsCompliant = !response.IsSuccess || (response.RawJson?.Contains("-32601", StringComparison.OrdinalIgnoreCase) == true)
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
        var response = await CallAsync(endpoint, "initialize", new
        {
            protocolVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(null),
            capabilities = new { },
            clientInfo = new { name = "mcp-benchmark", version = "1.0.0" }
        }, cancellationToken);
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
        return new TransportResult<CapabilitySummary>
        {
            Transport = initResult.Transport,
            IsSuccessful = initResult.IsSuccessful
        };
    }

    public async Task<ValidatorJsonRpcResponse> SendRawJsonAsync(string endpoint, string rawJson, CancellationToken cancellationToken, bool setContentType = true)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            return new ValidatorJsonRpcResponse { StatusCode = 503, IsSuccess = false, Error = "STDIO server not running." };
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _serverProcess.StandardInput.WriteLineAsync(rawJson.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            var responseLine = await ReadLineWithTimeoutAsync(_serverProcess.StandardOutput, TimeSpan.FromSeconds(10), cancellationToken);
            return new ValidatorJsonRpcResponse
            {
                StatusCode = string.IsNullOrEmpty(responseLine) ? 500 : 200,
                IsSuccess = !string.IsNullOrEmpty(responseLine),
                RawJson = responseLine,
                Error = string.IsNullOrEmpty(responseLine) ? "No response" : null
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

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
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
            ElapsedMs = elapsedMs
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
                ElapsedMs = elapsedMs
            };
        }
        catch (JsonException)
        {
            return new ValidatorJsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = responseLine,
                ElapsedMs = elapsedMs
            };
        }
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
