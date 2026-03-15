using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Professional console output service for MCP validation operations.
/// Provides structured, consistent messaging with appropriate formatting and progress indicators.
/// Follows industry standards for CLI application user experience.
/// </summary>
public interface IConsoleOutputService
{
    /// <summary>
    /// Displays a validation plan header with configuration details.
    /// </summary>
    /// <param name="title">The validation title</param>
    /// <param name="serverConfig">Server configuration to display</param>
    void DisplayValidationPlan(string title, McpServerConfig serverConfig);

    /// <summary>
    /// Shows a progress indicator for long-running operations.
    /// </summary>
    /// <param name="message">Progress message</param>
    /// <param name="showSpinner">Whether to show animated spinner</param>
    void ShowProgress(string message, bool showSpinner = true);

    /// <summary>
    /// Displays validation results with appropriate formatting and colors.
    /// </summary>
    /// <param name="results">Validation results to display</param>
    /// <param name="showDetails">Whether to show detailed information</param>
    void DisplayValidationResults(ValidationResult results, bool showDetails = false);

    /// <summary>
    /// Displays health check results with status indicators.
    /// </summary>
    /// <param name="result">Health check result</param>
    /// <param name="totalTime">Total execution time</param>
    /// <param name="verbose">Whether to show verbose information</param>
    void DisplayHealthCheckResults(HealthCheckResult result, TimeSpan totalTime, bool verbose = false);

    /// <summary>
    /// Shows success message with green formatting.
    /// </summary>
    /// <param name="message">Success message</param>
    void WriteSuccess(string message);

    /// <summary>
    /// Shows error message with red formatting.
    /// </summary>
    /// <param name="message">Error message</param>
    void WriteError(string message);

    /// <summary>
    /// Shows warning message with yellow formatting.
    /// </summary>
    /// <param name="message">Warning message</param>  
    void WriteWarning(string message);

    /// <summary>
    /// Shows informational message with standard formatting.
    /// </summary>
    /// <param name="message">Information message</param>
    void WriteInfo(string message);

    /// <summary>
    /// Displays a professional header section.
    /// </summary>
    /// <param name="title">Section title</param>
    /// <param name="color">Header color</param>
    void WriteHeader(string title, ConsoleColor color = ConsoleColor.Cyan);

    /// <summary>
    /// Displays configuration loading status.
    /// </summary>
    /// <param name="configPath">Configuration file path</param>
    /// <param name="loaded">Whether configuration was loaded successfully</param>
    void DisplayConfigurationStatus(string? configPath, bool loaded);

    /// <summary>
    /// Displays session metadata including session identifier and storage locations.
    /// </summary>
    void DisplaySessionBanner();

    /// <summary>
    /// Reminds the user where the session log is stored for additional troubleshooting details.
    /// </summary>
    /// <param name="context">Optional context string to prefix the message.</param>
    void WriteSessionLogHint(string? context = null);

    /// <summary>
    /// Sets the verbosity level of the output.
    /// </summary>
    /// <param name="verbose">Whether to show verbose output</param>
    void SetVerbose(bool verbose);

    /// <summary>
    /// Displays the discovery plan to the user.
    /// </summary>
    /// <param name="serverConfig">The server configuration to display.</param>
    /// <param name="format">The output format that will be used.</param>
    void DisplayDiscoveryPlan(McpServerConfig serverConfig, string format);

    /// <summary>
    /// Displays the discovered capabilities in the requested format.
    /// </summary>
    /// <param name="capabilities">The server capabilities to display.</param>
    /// <param name="format">The output format to use.</param>
    /// <param name="verbose">Whether to include verbose information.</param>
    void DisplayServerCapabilities(ServerCapabilities capabilities, string format, bool verbose);

    /// <summary>
    /// Displays the report generation plan to the user.
    /// </summary>
    /// <param name="inputFile">The input file being processed.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <param name="format">The report format.</param>
    /// <param name="validationResult">The validation results being reported on.</param>
    void DisplayReportPlan(FileInfo inputFile, string outputPath, string format, ValidationResult validationResult);
}
