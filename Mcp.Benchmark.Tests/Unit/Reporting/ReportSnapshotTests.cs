using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Tests.Fixtures;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public class ReportSnapshotTests
{
    private static readonly Regex MarkdownTimestampRegex = new(@"\*\*Generated:\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC", RegexOptions.Compiled);
    private static readonly Regex HtmlTimestampRegex = new(@"Generated on \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC", RegexOptions.Compiled);
    private static readonly Regex XmlTimestampRegex = new(@"<GeneratedAt>.*?</GeneratedAt>", RegexOptions.Compiled);

    private readonly MarkdownReportGenerator _markdownGenerator = new();
    private readonly ValidationReportRenderer _renderer = new();

    [Theory]
    [InlineData("markdown")]
    [InlineData("html")]
    [InlineData("xml")]
    [InlineData("sarif")]
    [InlineData("junit")]
    [InlineData("json")]
    public void Outputs_ShouldMatchApprovedSnapshots(string snapshotName)
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var actual = snapshotName switch
        {
            "markdown" => _markdownGenerator.GenerateReport(result),
            "html" => _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true),
            "xml" => _renderer.GenerateXmlReport(result, verbose: true),
            "sarif" => _renderer.GenerateSarifReport(result),
            "junit" => _renderer.GenerateJunitReport(result),
            "json" => JsonSerializer.Serialize(result.CloneWithoutSecrets(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(snapshotName), snapshotName, null)
        };

        var normalizedActual = Normalize(snapshotName, actual).TrimEnd('\r', '\n');

        // Allow snapshot regeneration via environment variable.
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(GetSnapshotSourcePath(snapshotName), normalizedActual);
            return;
        }

        var expected = File.ReadAllText(GetSnapshotPath(snapshotName)).Replace("\r\n", "\n").TrimEnd('\r', '\n');

        normalizedActual.Should().Be(expected);
    }

    [Fact]
    public void OfflineRenderers_ShouldBeByteStableForIdenticalCanonicalEvidence()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        _markdownGenerator.GenerateReport(result).Should().Be(_markdownGenerator.GenerateReport(result));
        _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true)
            .Should().Be(_renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true));
        _renderer.GenerateXmlReport(result, verbose: true).Should().Be(_renderer.GenerateXmlReport(result, verbose: true));
        _renderer.GenerateJunitReport(result).Should().Be(_renderer.GenerateJunitReport(result));
        _renderer.GenerateSarifReport(result).Should().Be(_renderer.GenerateSarifReport(result));
    }

    [Fact]
    public void OfflineRenderers_ShouldBeByteStableAcrossCultures()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var invariant = RenderAll(result);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            RenderAll(result).Should().Equal(invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private string[] RenderAll(ValidationResult result) =>
    [
        _markdownGenerator.GenerateReport(result),
        _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true),
        _renderer.GenerateXmlReport(result, verbose: true),
        _renderer.GenerateJunitReport(result),
        _renderer.GenerateSarifReport(result)
    ];

    private static string GetSnapshotPath(string snapshotName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReportSnapshots", $"{snapshotName}.snap");
    }

    private static string GetSnapshotSourcePath(string snapshotName)
    {
        // Walk up from bin/Debug/net8.0 to project root, then into Fixtures/ReportSnapshots.
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return Path.Combine(projectDir, "Fixtures", "ReportSnapshots", $"{snapshotName}.snap");
    }

    private static string Normalize(string snapshotName, string value)
    {
        var normalized = value.Replace("\r\n", "\n");
        return snapshotName switch
        {
            "markdown" => MarkdownTimestampRegex.Replace(normalized, "**Generated:** <normalized-timestamp>"),
            "html" => HtmlTimestampRegex.Replace(normalized, "Generated on <normalized-timestamp>"),
            "xml" => XmlTimestampRegex.Replace(normalized, "<GeneratedAt><normalized-timestamp></GeneratedAt>"),
            _ => normalized
        };
    }
}