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
    private Process? _serverProcess;
    private readonly SemaphoreSlim _lock = new(1, 1);
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

    /// <summary>
    /// Starts the STDIO server process from the endpoint (command string).
    /// Expected format: "command arg1 arg2" e.g. "npx -y @modelcontextprotocol/server-filesystem /tmp"
    /// </summary>
    public async Task StartProcessAsync(string command, Dictionary<string, string>? environment = null, CancellationToken ct = default)
    {
        var parts = ParseCommand(command);
        if (parts.Length == 0)
            throw new ArgumentException("STDIO endpoint must specify a command to execute.", nameof(command));

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

        for (int i = 1; i < parts.Length; i++)
            psi.ArgumentList.Add(parts[i]);

        if (environment != null)
        {
            foreach (var kvp in environment)
                psi.Environment[kvp.Key] = kvp.Value;
        }

        _logger.LogInformation("Starting STDIO MCP server: {Command}", command);
        _serverProcess = Process.Start(psi);

        if (_serverProcess == null || _serverProcess.HasExited)
            throw new InvalidOperationException($"Failed to start STDIO server process: {command}");

        // Give the process a moment to initialize
        await Task.Delay(500, ct);

        if (_serverProcess.HasExited)
        {
            var stderr = await _serverProcess.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"STDIO server exited immediately. stderr: {stderr}");
        }

        _logger.LogInformation("STDIO server process started (PID: {Pid})", _serverProcess.Id);
    }

    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return await CallAsync(endpoint, method, parameters, null, cancellationToken);
    }

    public async Task<ValidatorJsonRpcResponse> CallAsync(string endpoint, string method, object? parameters, AuthenticationConfig? authentication, CancellationToken cancellationToken = default)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
            return new ValidatorJsonRpcResponse { StatusCode = 503, IsSuccess = false, Error = "STDIO server process is not running." };

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
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Write to stdin (newline-delimited JSON-RPC)
            await _serverProcess.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _serverProcess.StandardInput.FlushAsync();

            // Read from stdout
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

            // Parse response
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
        // Not applicable for STDIO — return a synthetic 501 response
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.NotImplemented)
        {
            Content = new StringContent("SendAsync is not supported on STDIO transport.")
        };
        return Task.FromResult(response);
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
        var result = new JsonRpcErrorValidationResult();

        // Test: Invalid method
        var response = await CallAsync(endpoint, "rpc.invalid.method", null, cancellationToken);
        result.Tests.Add(new JsonRpcErrorTest
        {
            Name = "Invalid Method",
            IsValid = !response.IsSuccess || (response.RawJson?.Contains("-32601") == true),
            ExpectedErrorCode = -32601
        });

        return result;
    }

    public async Task<TransportResult<InitializeResult>> ValidateInitializeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
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
                    // Re-create with payload since Payload is init-only
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
            return new ValidatorJsonRpcResponse { StatusCode = 503, IsSuccess = false, Error = "STDIO server not running." };

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

    public void SetAuthentication(AuthenticationConfig? authentication) { /* No-op for STDIO */ }
    public void SetConcurrencyLimit(int maxConcurrency) { /* STDIO is inherently serial */ }
    public void SetProtocolVersion(string? protocolVersion) { /* No-op for STDIO */ }
    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    private static string[] ParseCommand(string command)
    {
        // Simple space-split that respects quoted strings
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        foreach (var ch in command)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ' ' && !inQuote && current.Length > 0) { parts.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        if (current.Length > 0) parts.Add(current.ToString());

        // On Windows, commands like "npx", "node", "python" need .cmd/.exe resolution
        if (parts.Count > 0 && !Path.HasExtension(parts[0]) && !Path.IsPathRooted(parts[0]))
        {
            // Try resolve via PATH by checking common extensions
            var resolved = ResolveExecutable(parts[0]);
            if (resolved != null) parts[0] = resolved;
        }

        return parts.ToArray();
    }

    /// <summary>
    /// Resolves an executable name to its full path.
    /// On Windows: checks .cmd, .exe, .bat extensions (npx → npx.cmd).
    /// On Linux/macOS: checks bare name first (npx → /usr/local/bin/npx).
    /// </summary>
    private static string? ResolveExecutable(string name)
    {
        var pathDirs = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);

        // Platform-aware extension order
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".cmd", ".exe", ".bat", "" }
            : new[] { "", ".sh" };

        foreach (var dir in pathDirs)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, name + ext);
                if (File.Exists(fullPath)) return fullPath;
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
            return null; // Timeout — not user cancellation
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.StandardInput.Close();
                if (!_serverProcess.WaitForExit(3000))
                {
                    _serverProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error shutting down STDIO server process");
            }
            _serverProcess.Dispose();
        }

        _lock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
