using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a service for rendering validation reports in various formats
/// from a ValidationResult snapshot.
/// </summary>
public interface IValidationReportRenderer
{
    /// <summary>
    /// Generates an HTML report for the given validation results.
    /// </summary>
    /// <param name="validationResult">The validation results to render.</param>
    /// <param name="reportConfig">Reporting configuration settings.</param>
    /// <param name="verbose">Whether to include verbose information.</param>
    /// <returns>Rendered HTML report content.</returns>
    string GenerateHtmlReport(ValidationResult validationResult, ReportingConfig reportConfig, bool verbose);

    /// <summary>
    /// Generates an XML report for the given validation results.
    /// </summary>
    /// <param name="validationResult">The validation results to render.</param>
    /// <param name="verbose">Whether to include verbose information.</param>
    /// <returns>Rendered XML report content.</returns>
    string GenerateXmlReport(ValidationResult validationResult, bool verbose);
}
