using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.CLI;

/// <summary>
/// Command handler for generating comprehensive reports from validation results.
/// Supports multiple output formats and detailed compliance reporting.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ReportCommand class.
/// </remarks>
/// <param name="consoleOutput">Professional console output service.</param>
/// <param name="logger">Logger instance for command execution logging.</param>
public class ReportCommand(
    IConsoleOutputService consoleOutput,
    ILogger<ReportCommand> logger,
    IValidationReportRenderer reportRenderer,
    INextStepAdvisor nextStepAdvisor,
    ISessionArtifactStore artifactStore)
{
    private readonly IConsoleOutputService _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
    private readonly ILogger<ReportCommand> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IValidationReportRenderer _reportRenderer = reportRenderer ?? throw new ArgumentNullException(nameof(reportRenderer));
    private readonly INextStepAdvisor _nextStepAdvisor = nextStepAdvisor ?? throw new ArgumentNullException(nameof(nextStepAdvisor));
    private readonly ISessionArtifactStore _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));

    /// <summary>
    /// Executes the report generation command with the specified parameters.
    /// </summary>
    /// <param name="inputFile">The validation results file to generate a report from.</param>
    /// <param name="format">Output format for the report (html, json, xml).</param>
    /// <param name="outputFile">Optional output file path (if not specified, generates based on input filename).</param>
    /// <param name="configFile">Optional configuration file for report customization.</param>
    /// <param name="verbose">Enable verbose logging output.</param>
    /// <returns>Task representing the asynchronous report generation operation.</returns>
    public async Task ExecuteAsync(FileInfo inputFile, string format, FileInfo? outputFile, FileInfo? configFile, bool verbose)
    {
        _nextStepAdvisor.Reset();
        try
        {
            _logger.LogInformation("Generating {Format} report from: {InputFile}", format.ToUpper(), inputFile.FullName);

            // Validate input file
            if (!inputFile.Exists)
            {
                throw new CliUsageException($"Input file not found: {inputFile.FullName}");
            }

            // Load validation results
            var validationResult = await LoadValidationResultsAsync(inputFile);

            // Always operate on a redacted clone when generating offline
            // reports so no raw tokens or secrets can leak into artifacts
            // even if the source JSON was produced by an older version.
            var safeResult = validationResult.CloneWithoutSecrets();

            // Determine output file path
            var outputPath = DetermineOutputPath(inputFile, outputFile, format);

            // Load report configuration if provided
            var reportConfig = await LoadReportConfigurationAsync(configFile);

            // Display report generation plan
            _consoleOutput.DisplayReportPlan(inputFile, outputPath, format, safeResult);

            // Generate report based on format
            await GenerateReportAsync(safeResult, outputPath, format, reportConfig, verbose);

            _consoleOutput.WriteSuccess($"Report generated successfully: {outputPath}");

            PersistSessionArtifact(inputFile, outputPath, format, safeResult);

            _logger.LogInformation("Report generated successfully: {OutputPath}", outputPath);
            Environment.ExitCode = 0;
        }
        catch (CliExceptionBase cliEx)
        {
            _consoleOutput.WriteError(cliEx.Message);
            _consoleOutput.WriteSessionLogHint("Report log");
            _nextStepAdvisor.SuggestSessionLogReview("Report log");
            _logger.LogWarning(cliEx, "Report generation aborted: {Message}", cliEx.Message);
            if (cliEx is CliUsageException && cliEx.Message.Contains("Input file not found", StringComparison.OrdinalIgnoreCase))
            {
                _nextStepAdvisor.AddSuggestion(
                    "Missing validation results",
                    new[]
                    {
                        "Pass the JSON snapshot produced by 'mcpval validate --output <folder>'.",
                        "Example: mcpval report --input <folder>/mcp-validation-*-result.json --format html"
                    });
            }
            Environment.ExitCode = cliEx.ExitCode;
        }
        catch (Exception ex)
        {
            _consoleOutput.WriteError($"Report generation failed: {ex.Message}");
            _consoleOutput.WriteSessionLogHint("Report log");
            _nextStepAdvisor.SuggestSessionLogReview("Report log");
            _logger.LogError(ex, "Report generation failed: {Message}", ex.Message);
            if (ex is FileNotFoundException)
            {
                _nextStepAdvisor.AddSuggestion(
                    "Missing validation results",
                    new[]
                    {
                        "Pass the JSON snapshot produced by 'mcpval validate --output <folder>'.",
                        "Example: mcpval report --input <folder>/mcp-validation-*-result.json --format html"
                    });
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            _nextStepAdvisor.Render();
        }
    }

    private void PersistSessionArtifact(FileInfo inputFile, string outputPath, string format, ValidationResult validationResult)
    {
        _artifactStore.TrySaveJson(
            "report-results",
            new
            {
                timestamp = DateTimeOffset.UtcNow,
                input = inputFile.FullName,
                output = outputPath,
                format,
                validationResult
            });
    }

    /// <summary>
    /// Loads validation results from the input file.
    /// </summary>
    /// <param name="inputFile">
    /// The file containing validation results. This can be either the JSON
    /// ValidationResult snapshot (e.g. <c>mcp-validation-*-result.json</c>) or
    /// the Markdown report produced by <c>mcpval validate</c>
    /// (e.g. <c>mcp-validation-*-report.md</c>). In the Markdown case, the
    /// corresponding JSON file is automatically resolved and loaded.
    /// </param>
    /// <returns>Deserialized validation results.</returns>
    private async Task<ValidationResult> LoadValidationResultsAsync(FileInfo inputFile)
    {
        _logger.LogDebug("Loading validation results from: {InputFile}", inputFile.FullName);

        // Allow callers to pass either the JSON result file or the Markdown
        // report path. For Markdown input we resolve the sibling
        // "*-result.json" file written by the validate command.
        FileInfo jsonSourceFile = inputFile;

        if (string.Equals(inputFile.Extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            var directory = inputFile.DirectoryName ?? Directory.GetCurrentDirectory();
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(inputFile.Name);

            // Our validate command saves files as:
            //   mcp-validation-{timestamp}-report.md
            //   mcp-validation-{timestamp}-result.json
            // If the name ends with "-report", swap it for "-result".
            const string reportSuffix = "-report";
            var baseName = nameWithoutExtension.EndsWith(reportSuffix, StringComparison.OrdinalIgnoreCase)
                ? nameWithoutExtension[..^reportSuffix.Length]
                : nameWithoutExtension;

            var expectedJsonPath = Path.Combine(directory, $"{baseName}-result.json");

            if (!File.Exists(expectedJsonPath))
            {
                throw new CliOperationException(
                    "Markdown report files require a matching JSON validation result file. " +
                    "Expected to find: " + expectedJsonPath + ". " +
                    "Re-run 'mcpval validate --output <folder>' or pass the JSON file directly.");
            }

            jsonSourceFile = new FileInfo(expectedJsonPath);
            _logger.LogInformation(
                "Resolved Markdown report input to JSON results file: {JsonFile}",
                jsonSourceFile.FullName);
        }

        var json = await File.ReadAllTextAsync(jsonSourceFile.FullName);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var result = DeserializeWithCapabilityFallback(json, options);
        _logger.LogDebug("Loaded validation results for validation ID: {ValidationId}", result.ValidationId);
        return result;
    }

    private ValidationResult DeserializeWithCapabilityFallback(string json, JsonSerializerOptions options)
    {
        try
        {
            return DeserializeOrThrow(json, options);
        }
        catch (InvalidOperationException ex) when (IsCapabilitySnapshotSerializationIssue(ex))
        {
            _logger.LogWarning(ex, "Capability snapshot contained SDK types that cannot be replayed offline. Stripping tool payload for report rendering.");
            var sanitizedJson = SanitizeCapabilitySnapshot(json);
            return DeserializeOrThrow(sanitizedJson, options);
        }
    }

    private static ValidationResult DeserializeOrThrow(string json, JsonSerializerOptions options)
    {
        var result = JsonSerializer.Deserialize<ValidationResult>(json, options);
        if (result == null)
        {
            throw new CliOperationException("Failed to deserialize validation results");
        }

        return result;
    }

    private static bool IsCapabilitySnapshotSerializationIssue(Exception ex)
    {
        return ex.Message.IndexOf("McpClientTool", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SanitizeCapabilitySnapshot(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
            {
                return json;
            }

            if (root["capabilitySnapshot"] is JsonObject snapshot &&
                snapshot["payload"] is JsonObject payload &&
                payload.ContainsKey("tools"))
            {
                payload["tools"] = new JsonArray();
            }

            return root.ToJsonString(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Loads report configuration from file if provided.
    /// </summary>
    /// <param name="configFile">Optional configuration file.</param>
    /// <returns>Report configuration or default configuration if no file provided.</returns>
    private async Task<ReportingConfig> LoadReportConfigurationAsync(FileInfo? configFile)
    {
        if (configFile?.Exists == true)
        {
            _logger.LogDebug("Loading report configuration from: {ConfigFile}", configFile.FullName);
            var json = await File.ReadAllTextAsync(configFile.FullName);
            var fullConfig = JsonSerializer.Deserialize<McpValidatorConfiguration>(json);
            return fullConfig?.Reporting ?? new ReportingConfig();
        }

        return new ReportingConfig();
    }

    /// <summary>
    /// Determines the output file path based on input parameters.
    /// </summary>
    /// <param name="inputFile">The input validation results file.</param>
    /// <param name="outputFile">Optional specified output file.</param>
    /// <param name="format">The report format.</param>
    /// <returns>The determined output file path.</returns>
    private string DetermineOutputPath(FileInfo inputFile, FileInfo? outputFile, string format)
    {
        if (outputFile != null)
        {
            return outputFile.FullName;
        }

        var extension = format.ToLowerInvariant() switch
        {
            "html" => ".html",
            "json" => ".json",
            "xml" => ".xml",
            _ => ".txt"
        };

        var baseName = Path.GetFileNameWithoutExtension(inputFile.Name);
        var directory = inputFile.DirectoryName ?? Directory.GetCurrentDirectory();
        return Path.Combine(directory, $"{baseName}-report{extension}");
    }

    /// <summary>
    /// Generates the report in the specified format.
    /// </summary>
    /// <param name="validationResult">The validation results to report on.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <param name="format">The report format.</param>
    /// <param name="reportConfig">Report configuration settings.</param>
    /// <param name="verbose">Whether to include verbose information.</param>
    /// <returns>Task representing the asynchronous report generation.</returns>
    private async Task GenerateReportAsync(ValidationResult validationResult, string outputPath, string format, ReportingConfig reportConfig, bool verbose)
    {
        // Always generate detailed reports for offline artifacts.
        // The verbose flag is kept for backwards compatibility but
        // we treat all report formats as detailed views.
        verbose = true;

        switch (format.ToLowerInvariant())
        {
            case "html":
            {
                var html = _reportRenderer.GenerateHtmlReport(validationResult, reportConfig, verbose);
                await File.WriteAllTextAsync(outputPath, html);
                break;
            }
            case "xml":
            {
                var xml = _reportRenderer.GenerateXmlReport(validationResult, verbose);
                await File.WriteAllTextAsync(outputPath, xml);
                break;
            }
            default:
                throw new CliUsageException($"Unsupported report format: {format}");
        }
    }
}
