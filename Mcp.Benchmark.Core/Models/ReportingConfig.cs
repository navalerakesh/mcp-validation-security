using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Configuration for test result reporting and output formatting.
/// Supports multiple output formats and detailed compliance reporting.
/// </summary>
public class ReportingConfig
{
    /// <summary>
    /// Gets or sets the output directory for test reports.
    /// </summary>
    public string OutputDirectory { get; set; } = "./mcp-validation-reports";

    /// <summary>
    /// Gets or sets the report formats to generate.
    /// </summary>
    public List<ReportFormat> Formats { get; set; } = new() { ReportFormat.Json, ReportFormat.Html };

    /// <summary>
    /// Gets or sets the human-facing report detail level.
    /// Full remains compact, but includes all report sections.
    /// Minimal keeps the executive-only view.
    /// </summary>
    [JsonPropertyName("detailLevel")]
    public ReportDetailLevel DetailLevel { get; set; } = ReportDetailLevel.Full;

    /// <summary>
    /// Gets or sets whether to include detailed test execution logs.
    /// Legacy switch retained for compatibility; explicit detail level is preferred.
    /// </summary>
    public bool IncludeDetailedLogs { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include performance metrics in reports.
    /// </summary>
    public bool IncludePerformanceMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include security vulnerability details.
    /// </summary>
    public bool IncludeSecurityDetails { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to generate compliance summary reports.
    /// </summary>
    public bool GenerateComplianceSummary { get; set; } = true;

    /// <summary>
    /// Gets or sets custom report templates to use.
    /// </summary>
    public Dictionary<string, string> CustomTemplates { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum log level to include in reports.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets the MCP spec profile used for this run (e.g., 2025-06-18, 2025-11-25, latest).
    /// </summary>
    [JsonPropertyName("specProfile")]
    public string SpecProfile { get; set; } = "latest";

    /// <summary>
    /// Gets or sets whether to include spec references and check IDs in outputs.
    /// </summary>
    [JsonPropertyName("includeSpecReferences")]
    public bool IncludeSpecReferences { get; set; } = true;

    /// <summary>
    /// Resolves the effective human-facing report detail level.
    /// </summary>
    public ReportDetailLevel GetEffectiveDetailLevel()
    {
        return IncludeDetailedLogs ? ReportDetailLevel.Full : DetailLevel;
    }

    /// <summary>
    /// Returns whether rendered reports should include all major sections.
    /// </summary>
    public bool IncludesDetailedSections()
    {
        return GetEffectiveDetailLevel() == ReportDetailLevel.Full;
    }

    /// <summary>
    /// Applies an explicit report detail level and keeps the legacy detailed flag aligned.
    /// </summary>
    public void ApplyDetailLevel(ReportDetailLevel detailLevel)
    {
        DetailLevel = detailLevel;
        IncludeDetailedLogs = detailLevel == ReportDetailLevel.Full;
    }

    /// <summary>
    /// Normalizes legacy and current detail flags after configuration loading.
    /// </summary>
    public void NormalizeDetailLevel()
    {
        IncludeDetailedLogs = IncludesDetailedSections();
    }
}

/// <summary>
/// High-level detail level for generated human-facing reports.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportDetailLevel
{
    Minimal,
    Full
}

/// <summary>
/// Enumeration of supported report output formats.
/// </summary>
public enum ReportFormat
{
    /// <summary>
    /// JSON format for programmatic consumption.
    /// </summary>
    Json,

    /// <summary>
    /// HTML format for human-readable reports.
    /// </summary>
    Html,

    /// <summary>
    /// XML format for integration with CI/CD systems.
    /// </summary>
    Xml,

    /// <summary>
    /// CSV format for data analysis.
    /// </summary>
    Csv,

    /// <summary>
    /// Plain text format for console output.
    /// </summary>
    Text,

    /// <summary>
    /// JUnit XML format for test result integration.
    /// </summary>
    JUnit,

    /// <summary>
    /// SARIF format for code scanning and CI integration.
    /// </summary>
    Sarif
}

/// <summary>
/// Enumeration of logging levels for report filtering.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Trace level - most verbose logging.
    /// </summary>
    Trace,

    /// <summary>
    /// Debug level - detailed debugging information.
    /// </summary>
    Debug,

    /// <summary>
    /// Information level - general informational messages.
    /// </summary>
    Information,

    /// <summary>
    /// Warning level - potential issues or important notices.
    /// </summary>
    Warning,

    /// <summary>
    /// Error level - error conditions that don't stop execution.
    /// </summary>
    Error,

    /// <summary>
    /// Critical level - critical errors that may cause termination.
    /// </summary>
    Critical
}

/// <summary>
/// Configuration for test execution behavior and control flow.
/// </summary>
public class TestExecutionConfig
{
    /// <summary>
    /// Gets or sets whether to continue testing after encountering failures.
    /// </summary>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to run tests in parallel where possible.
    /// </summary>
    public bool EnableParallelExecution { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of parallel test threads.
    /// Defaults to 10 to avoid overwhelming MCP servers by default.
    /// </summary>
    public int MaxParallelThreads { get; set; } = 10;

    /// <summary>
    /// Gets or sets the default timeout for individual tests in milliseconds.
    /// </summary>
    public int DefaultTestTimeoutMs { get; set; } = 120000;

    /// <summary>
    /// Gets or sets the global timeout for the entire test suite in minutes.
    /// </summary>
    public int GlobalTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the number of retry attempts for flaky tests.
    /// </summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts in milliseconds.
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to randomize test execution order.
    /// </summary>
    public bool RandomizeExecutionOrder { get; set; } = false;

    /// <summary>
    /// Gets or sets specific test categories to include (empty means all).
    /// </summary>
    public List<string> IncludeCategories { get; set; } = new();

    /// <summary>
    /// Gets or sets specific test categories to exclude.
    /// </summary>
    public List<string> ExcludeCategories { get; set; } = new();

    /// <summary>
    /// Gets or sets custom test filters using tag expressions.
    /// </summary>
    public List<string> CustomFilters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to enable real-time progress reporting.
    /// </summary>
    public bool EnableProgressReporting { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to capture screenshots/snapshots for UI-related tests.
    /// </summary>
    public bool CaptureSnapshots { get; set; } = false;

    /// <summary>
    /// Gets or sets environment variables to set during test execution.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
}
