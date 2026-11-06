using AngleSharp.Html.Parser;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Tests.Fixtures;
using System.Text.RegularExpressions;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public sealed class ValidationHtmlAccessibilityTests
{
    [Fact]
    public void FullReport_ShouldProvideStructuralAccessibilityAndSafeInteractions()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var html = new ValidationReportRenderer().GenerateHtmlReport(
            result,
            result.ValidationConfig.Reporting,
            verbose: true);
        var document = new HtmlParser().ParseDocument(html);

        document.DocumentElement!.GetAttribute("lang").Should().Be("en");
        document.QuerySelectorAll("h1").Should().ContainSingle();
        var ids = document.QuerySelectorAll("[id]").Select(element => element.Id).ToArray();
        ids.Should().OnlyHaveUniqueItems();

        foreach (var element in document.QuerySelectorAll("[aria-labelledby]"))
        {
            foreach (var id in element.GetAttribute("aria-labelledby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                document.GetElementById(id).Should().NotBeNull($"aria-labelledby must reference existing id '{id}'");
            }
        }

        document.QuerySelectorAll("details").Should().NotBeEmpty();
        document.QuerySelectorAll("details").Should().OnlyContain(details => details.QuerySelector(":scope > summary") != null);
        document.QuerySelectorAll("table").Should().NotBeEmpty();
        document.QuerySelectorAll("table").Should().OnlyContain(table => table.QuerySelectorAll("th").Length > 0);
        document.QuerySelectorAll("a[href]").All(link =>
        {
            if (!Uri.TryCreate(link.GetAttribute("href"), UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }).Should().BeTrue();
        document.QuerySelectorAll("script").Should().BeEmpty();
        document.QuerySelectorAll("[tabindex^='-']").Should().BeEmpty();
        document.QuerySelectorAll(".producer-signature__divider")
            .Should().OnlyContain(element => element.GetAttribute("aria-hidden") == "true");
        html.Should().Contain("@media print");
        html.ToLowerInvariant().Should().NotContain("onclick=");
    }

    [Fact]
    public void ThemeTextTokens_ShouldMeetWcagAaContrast()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var html = new ValidationReportRenderer().GenerateHtmlReport(
            result,
            result.ValidationConfig.Reporting,
            verbose: true);

        Contrast(GetCssColor(html, "text-soft"), GetCssColor(html, "surface-strong"))
            .Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(GetCssColor(html, "text-soft"), GetCssColor(html, "surface-muted"))
            .Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(GetCssColor(html, "tone-warning"), GetCssColor(html, "tone-warning-soft"))
            .Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void HostileTargetText_ShouldRemainTextAndNeverCreateActiveElements()
    {
        var result = new ValidationResult
        {
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://example.test/<script>alert(1)</script>",
                Transport = "http"
            }
        };
        result.Assurance = new AssuranceEvaluation
        {
            Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Inventory,
            Claims =
            [
                new AssuranceClaim
                {
                    ClaimId = "hostile",
                    Subject = "<img src=x onerror=alert(1)>",
                    Dimension = AssuranceDimension.Security,
                    RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                    Authority = AssuranceAuthority.Heuristic,
                    Source = new AssuranceSourceReference { Uri = "javascript:alert(1)", Revision = "test" },
                    Outcome = AssuranceClaimOutcome.ReviewRequired,
                    Summary = "<script>alert(2)</script>"
                }
            ],
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = "inventory",
                Status = CertificateEligibilityStatus.Ineligible
            }
        };
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, new ReportingConfig(), verbose: true);
        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelectorAll("script,img").Should().BeEmpty();
        document.QuerySelectorAll("a[href^='javascript:'],a[href^='data:']").Should().BeEmpty();
        document.Body!.TextContent.Should().Contain("<script>alert(2)</script>");
    }

    private static (int Red, int Green, int Blue) GetCssColor(string html, string name)
    {
        var match = Regex.Match(html, $@"--{Regex.Escape(name)}:\s*#(?<hex>[0-9a-fA-F]{{6}})");
        match.Success.Should().BeTrue($"CSS variable --{name} must be present");
        var hex = match.Groups["hex"].Value;
        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..], 16));
    }

    private static double Contrast(
        (int Red, int Green, int Blue) foreground,
        (int Red, int Green, int Blue) background)
    {
        var foregroundLuminance = Luminance(foreground);
        var backgroundLuminance = Luminance(background);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
            (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double Luminance((int Red, int Green, int Blue) color) =>
        0.2126 * Linearize(color.Red) +
        0.7152 * Linearize(color.Green) +
        0.0722 * Linearize(color.Blue);

    private static double Linearize(int channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
