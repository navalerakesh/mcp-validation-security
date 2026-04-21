using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.CLI.Services.Formatters;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services;

/// <summary>
/// Professional console output service for displaying validation results with appropriate formatting.
/// Provides clean, readable output with strategic emoji usage for enhanced user experience.
/// </summary>
public class ConsoleOutputService : IConsoleOutputService
{
    private readonly bool _useColors;
    private readonly CliSessionContext _sessionContext;
    private bool _verbose;
    private bool _sessionBannerDisplayed;

    public ConsoleOutputService(CliSessionContext sessionContext)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _useColors = !Console.IsOutputRedirected;
        _verbose = false;
    }

    public void SetVerbose(bool verbose)
    {
        _verbose = verbose;
    }

    /// <summary>
    /// Displays the main validation results in a professional, clean format.
    /// </summary>
    public void DisplayValidationResults(ValidationResult result, bool showDetails = false)
    {
        ValidationFormatter.DisplayResults(result, showDetails, _useColors, _verbose);
    }

    public void WriteError(string message)
    {
        FormatterUtils.WriteLineWithColor($"❌ Error: {message}", ConsoleColor.Red, _useColors);
    }

    public void WriteWarning(string message)
    {
        FormatterUtils.WriteLineWithColor($"⚠️ Warning: {message}", ConsoleColor.Yellow, _useColors);
    }

    public void WriteInfo(string message)
    {
        if (_verbose)
        {
            FormatterUtils.WriteLineWithColor($"ℹ️ {message}", ConsoleColor.White, _useColors);
        }
    }

    public void WriteSuccess(string message)
    {
        FormatterUtils.WriteLineWithColor($"✅ {message}", ConsoleColor.Green, _useColors);
    }

    public void DisplayValidationPlan(string title, McpServerConfig serverConfig)
    {
        DisplaySessionBanner();
        Console.WriteLine();
        WriteHeader(title.ToUpperInvariant());

        WriteInfo($"Server: {serverConfig?.Endpoint ?? "unknown"}");
        WriteInfo($"Transport: {serverConfig?.Transport ?? "unknown"}");
        WriteInfo($"Timeout: {serverConfig?.TimeoutMs ?? 0}ms");

        if (!string.IsNullOrEmpty(serverConfig?.Authentication?.Token))
        {
            var tokenPreview = serverConfig!.Authentication!.Token.Length > 8
                ? $"{serverConfig.Authentication.Token[..4]}...{serverConfig.Authentication.Token[^4..]}"
                : "****";
            WriteInfo($"Authentication: Bearer {tokenPreview}");
        }

        Console.WriteLine();
        ShowProgress("Initializing validation...", false);
    }

    public void ShowProgress(string message, bool showSpinner = true)
    {
        if (showSpinner)
        {
            Console.Write($"{message} ");
            FormatterUtils.WriteWithColor("⏳", ConsoleColor.Yellow, _useColors);
            Console.WriteLine();
        }
        else
        {
            WriteInfo(message);
        }
    }

    public void DisplayHealthCheckResults(HealthCheckResult result, TimeSpan totalTime, bool verbose = false)
    {
        Console.WriteLine();

        var statusLabel = GetHealthStatusLabel(result);

        if (result.IsHealthy)
        {
            WriteSuccess("Server is healthy");
        }
        else if (result.Disposition == HealthCheckDisposition.Protected)
        {
            WriteWarning("Server is reachable but protected");
        }
        else if (result.Disposition == HealthCheckDisposition.TransientFailure)
        {
            WriteWarning("Health check encountered transient capacity or transport constraints");
        }
        else if (result.Disposition == HealthCheckDisposition.Inconclusive)
        {
            WriteWarning("Server responded, but the health handshake was inconclusive");
        }
        else
        {
            WriteError("Server is unhealthy");
        }

        Console.WriteLine();
        WriteHeader("Health Check Results");
        WriteInfo($"Status: {statusLabel}");
        WriteInfo($"Response Time: {result.ResponseTimeMs:F1}ms");
        WriteInfo($"Total Check Time: {totalTime.TotalMilliseconds:F1}ms");

        if (!string.IsNullOrEmpty(result.ServerVersion))
        {
            WriteInfo($"Server Version: {result.ServerVersion}");
        }

        if (!string.IsNullOrEmpty(result.ProtocolVersion))
        {
            WriteInfo($"Protocol Version: {result.ProtocolVersion}");
        }

        var handshake = result.InitializationDetails;
        var handshakePayload = handshake?.Payload;
        if (!string.IsNullOrWhiteSpace(handshakePayload?.ServerInfo?.Name))
        {
            var versionSuffix = string.IsNullOrWhiteSpace(handshakePayload.ServerInfo?.Version)
                ? string.Empty
                : $" v{handshakePayload.ServerInfo!.Version}";
            WriteInfo($"Implementation: {handshakePayload.ServerInfo!.Name}{versionSuffix}");
        }

        if (handshake != null)
        {
            var handshakeDuration = handshake.Transport.Duration.TotalMilliseconds;
            if (handshakeDuration > 0)
            {
                WriteInfo($"Handshake Duration: {handshakeDuration:F1}ms");
            }

            if (handshake.Transport.StatusCode.HasValue)
            {
                WriteInfo($"Handshake HTTP Status: {handshake.Transport.StatusCode}");
            }

            if (verbose && handshakePayload?.Capabilities != null)
            {
                var capabilityFlags = new List<string>();
                if (handshakePayload.Capabilities.Tools != null) capabilityFlags.Add("tools");
                if (handshakePayload.Capabilities.Resources != null) capabilityFlags.Add("resources");
                if (handshakePayload.Capabilities.Prompts != null) capabilityFlags.Add("prompts");
                if (handshakePayload.Capabilities.Logging != null) capabilityFlags.Add("logging");
                if (handshakePayload.Capabilities.Completions != null) capabilityFlags.Add("completions");
                if (capabilityFlags.Count > 0)
                {
                    WriteInfo($"Declared Capabilities: {string.Join(", ", capabilityFlags)}");
                }
            }
        }

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            WriteError($"Error: {result.ErrorMessage}");
        }

        if (verbose && result.ServerMetadata?.Count > 0)
        {
            Console.WriteLine();
            WriteHeader("Server Metadata");
            foreach (var metadata in result.ServerMetadata)
            {
                WriteInfo($"{metadata.Key}: {metadata.Value}");
            }
        }

        Console.WriteLine();
    }

    private static string GetHealthStatusLabel(HealthCheckResult result)
    {
        return result.Disposition switch
        {
            HealthCheckDisposition.Healthy => "Healthy",
            HealthCheckDisposition.Protected => "Reachable (Protected)",
            HealthCheckDisposition.TransientFailure => "Transient Failure",
            HealthCheckDisposition.Inconclusive => "Inconclusive",
            HealthCheckDisposition.Unhealthy => "Unhealthy",
            _ => result.IsHealthy ? "Healthy" : "Unhealthy"
        };
    }

    public void WriteHeader(string title, ConsoleColor color = ConsoleColor.Cyan)
    {
        FormatterUtils.WriteLineWithColor(title, color, _useColors);
    }

    public void DisplayConfigurationStatus(string? configPath, bool loaded)
    {
        if (configPath != null)
        {
            if (loaded)
            {
                WriteSuccess($"Configuration loaded from: {configPath}");
                WriteInfo("Configuration successfully applied");
            }
            else
            {
                WriteError($"Invalid configuration file: {configPath}");
            }
        }
        else
        {
            WriteInfo("Using default configuration");
        }
    }

    public void DisplayDiscoveryPlan(McpServerConfig serverConfig, string format)
    {
        DisplaySessionBanner();
        DiscoveryFormatter.DisplayPlan(serverConfig, format,
            (text, color) => FormatterUtils.WriteLineWithColor(text, color, _useColors),
            WriteInfo);
    }

    public void DisplayServerCapabilities(ServerCapabilities capabilities, string format, bool verbose)
    {
        CapabilityFormatter.Display(capabilities, format, verbose,
            (text, color) => FormatterUtils.WriteWithColor(text, color, _useColors));
    }

    public void DisplayReportPlan(FileInfo inputFile, string outputPath, string format, ValidationResult validationResult)
    {
        DisplaySessionBanner();
        Console.WriteLine();
        WriteHeader("REPORT GENERATION");

        WriteInfo($"Input File: {inputFile.Name}");
        WriteInfo($"Output File: {Path.GetFileName(outputPath)}");
        WriteInfo($"Format: {format.ToUpper()}");
        WriteInfo($"Validation ID: {validationResult.ValidationId}");
        WriteInfo($"Server: {validationResult.ServerConfig.Endpoint}");
        WriteInfo($"Overall Status: {validationResult.OverallStatus}");
        WriteInfo($"Compliance Score: {validationResult.ComplianceScore:F1}%");
        Console.WriteLine();
        Console.Write("Generating report... ");
    }

    public void DisplaySessionBanner()
    {
        if (_sessionBannerDisplayed)
        {
            return;
        }

        _sessionBannerDisplayed = true;

        Console.WriteLine();
        WriteHeader("SESSION", ConsoleColor.Magenta);
        FormatterUtils.WriteLineWithColor($"Session ID: {_sessionContext.SessionId}", ConsoleColor.White, _useColors);
        FormatterUtils.WriteLineWithColor($"State Path: {_sessionContext.StateDirectory}", ConsoleColor.White, _useColors);
        FormatterUtils.WriteLineWithColor($"Log File: {_sessionContext.LogFilePath}", ConsoleColor.White, _useColors);
        Console.WriteLine();
    }

    public void WriteSessionLogHint(string? context = null)
    {
        var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : context.EndsWith(':') ? context + ' ' : context + ": ";
        FormatterUtils.WriteLineWithColor(
            $"{prefix}See session log for details: {_sessionContext.LogFilePath}",
            ConsoleColor.DarkGray,
            _useColors);
    }
}
