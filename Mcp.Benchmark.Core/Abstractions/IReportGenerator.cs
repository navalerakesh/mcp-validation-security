using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a service for generating validation reports.
/// </summary>
public interface IReportGenerator
{
    /// <summary>
    /// Generates a formatted report from the validation results.
    /// </summary>
    /// <param name="result">The validation results to report on.</param>
    /// <returns>The generated report as a string.</returns>
    string GenerateReport(ValidationResult result);
}
