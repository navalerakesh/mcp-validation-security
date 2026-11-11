using AngleSharp.Html.Parser;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Tests.Fixtures;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public sealed class ValidationHtmlAccessibilityTests
{
    [Fact]
    public void LargeReport_ShouldRetainExactClaimAndToolInventories()
    {
        var result = ReportSnapshotTestData.CreateLargeReportResult().CloneWithoutSecrets();
        result.Assurance.Claims.Should().HaveCount(10_000);
        result.Assurance.Claims.Select(claim => claim.ClaimId).Should().OnlyHaveUniqueItems();
        result.ToolValidation!.ToolsDiscovered.Should().Be(1000);
        result.ToolValidation.ToolResults.Should().HaveCount(1000);
        result.ToolValidation.ToolResults.Select(tool => tool.ToolName).Should().OnlyHaveUniqueItems();
        result.ToolValidation.ToolResults.Skip(2).Should().OnlyContain(tool => tool.Status == TestStatus.Skipped);
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);
        var document = new HtmlParser().ParseDocument(html);
        document.QuerySelectorAll("article[data-claim-id]").Should().HaveCount(10_000);
        document.QuerySelectorAll("article[data-claim-id][hidden]").Should().BeEmpty();
        var text = document.Body!.TextContent;
        foreach (var tool in result.ToolValidation.ToolResults)
            text.Should().Contain(tool.ToolName);

        var output = Environment.GetEnvironmentVariable("MCPVAL_REPORT_SCALE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.Exists(output).Should().BeFalse("scale evidence must not overwrite an earlier run");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "report.html"), html);
            File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(
                result.CloneWithoutSecrets(), Mcp.Benchmark.CLI.Utilities.ArtifactJsonOptions.Create()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteClaimSummary_ShouldAgreeWithAllCanonicalIncompleteClaims(bool verbose)
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var expected = result.Assurance.Claims.Where(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete)
            .Select(claim => claim.ClaimId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        expected.Should().NotBeEmpty("the fixture includes a canonical incomplete claim outside the narrowed assessment lists");
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose);
        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelectorAll("#recorded-incomplete-claims li").Select(element => element.TextContent).Should().Equal(expected);
        document.GetElementById("recorded-incomplete-claims")!.TextContent.Should().NotContain("None recorded.");
    }

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\r")]
    public void ReportPolicy_ShouldAllowOnlyExactStylesheetAndFixedLocalScript(bool verbose, string lineEnding)
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose);
        var document = new HtmlParser().ParseDocument(html.ReplaceLineEndings(lineEnding));
        var stylesheet = document.QuerySelectorAll("style").Should().ContainSingle().Subject.TextContent;
        var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(stylesheet)));
        var script = document.QuerySelectorAll("script").Should().ContainSingle().Subject.TextContent;
        var expectedScriptHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var policy = document.QuerySelector("meta[http-equiv='Content-Security-Policy']")!.GetAttribute("content");

        policy.Should().Contain($"style-src 'sha256-{expectedHash}'");
        policy.Should().Contain("default-src 'none'");
        policy.Should().Contain($"script-src 'sha256-{expectedScriptHash}'");
        policy.Should().Contain("base-uri 'none'");
        policy.Should().Contain("form-action 'none'");
        policy.Should().NotContain("unsafe-inline");
        document.QuerySelectorAll("[style],iframe,object,form").Should().BeEmpty();
        document.GetElementById("claim-filters")!.HasAttribute("hidden").Should().BeTrue();
        document.QuerySelectorAll("article[data-claim-id][hidden]").Should().BeEmpty();
        script.Should().NotContain("innerHTML").And.NotContain("fetch(").And.NotContain("eval(");
    }

    [Theory]
    [InlineData(false, ValidationStatus.Passed, "1")]
    [InlineData(true, ValidationStatus.Passed, "1")]
    [InlineData(false, ValidationStatus.Cancelled, "124")]
    public void ReportOpening_ShouldRetainPolicyFailureExitAndExactErrorCounts(bool verbose, ValidationStatus status, string exit)
    {
        var outcomes = new[] { AssuranceClaimOutcome.Satisfied, AssuranceClaimOutcome.Violated }
            .Concat(Enumerable.Repeat(AssuranceClaimOutcome.Error, 9));
        var result = new ValidationResult
        {
            OverallStatus = status,
            PolicyOutcome = new ValidationPolicyOutcome { Passed = false, Summary = "Synthetic deployment policy rejected this scope." },
            Assurance = new AssuranceEvaluation
            {
                Profile = AssuranceProfileCatalog.Ci,
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "ci", Status = CertificateEligibilityStatus.Ineligible
                },
                Assessments = new AssuranceAssessmentSet
                {
                    CoreConformance = new CoreConformanceAssessment { Status = CoreConformanceStatus.Conformant },
                    SecurityAssurance = new SecurityAssuranceAssessment { Status = SecurityAssuranceStatus.Verified },
                    OperationalReadiness = new OperationalReadinessAssessment { Status = OperationalReadinessStatus.Ready }
                },
                Claims = outcomes.Select((outcome, index) => new AssuranceClaim
                {
                    ClaimId = $"claim-{index}", Subject = $"subject-{index}", Outcome = outcome,
                    Dimension = AssuranceDimension.ExecutionIntegrity, RequirementLevel = AssuranceRequirementLevel.Policy,
                    Authority = AssuranceAuthority.ProviderPolicy, Source = new AssuranceSourceReference { Revision = "test" },
                    Summary = $"Recorded {outcome} for subject {index}."
                }).ToArray()
            }
        };
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose);
        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll("#assessment-decision .assessment-facts > div");

        facts.Single(row => row.QuerySelector("dt")!.TextContent == "Policy outcome").QuerySelector("dd")!.TextContent.Should().Be("Failed");
        facts.Single(row => row.QuerySelector("dt")!.TextContent == "Recommended exit").QuerySelector("dd")!.TextContent.Should().Be(exit);
        document.GetElementById("assessment-decision")!.TextContent.Should().Contain(result.PolicyOutcome.Summary);
        var counts = document.QuerySelectorAll("#claim-outcomes tbody tr");
        counts.Single(row => row.QuerySelector("th")!.TextContent == "Error").QuerySelector("td")!.TextContent.Should().Be("9");
        counts.Single(row => row.QuerySelector("th")!.TextContent == "Violated").QuerySelector("td")!.TextContent.Should().Be("1");
        counts.Single(row => row.QuerySelector("th")!.TextContent == "Incomplete").QuerySelector("td")!.TextContent.Should().Be("0");
        document.GetElementById("claim-outcomes")!.Closest("details").Should().BeNull();
        var claims = document.QuerySelectorAll("article[data-claim-id]");
        claims.Should().HaveCount(result.Assurance.Claims.Count);
        foreach (var claim in result.Assurance.Claims)
        {
            var entry = claims.Single(element => element.GetAttribute("data-claim-id") == claim.ClaimId);
            entry.Id.Should().NotBeNullOrWhiteSpace();
            entry.Closest("details").Should().BeNull();
            entry.TextContent.Should().Contain(claim.Summary);
            entry.TextContent.Should().Contain(claim.Outcome.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportOpening_ShouldBeStaticDocumentWithDecisionAndActionsBeforeTechnicalEvidence(bool verbose)
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose);
        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelectorAll("main").Should().ContainSingle();
        document.QuerySelector("h1")!.TextContent.Should().Be("MCPVal assessment");
        foreach (var id in new[] { "assessment-decision", "evidence-and-actions" })
        {
            var section = document.GetElementById(id);
            section.Should().NotBeNull();
            section!.Closest("details").Should().BeNull("decision-critical content must not require disclosure");
            section.QuerySelectorAll(".panel,.brief-grid,.status-panel").Should().BeEmpty();
        }
        html.IndexOf("id=\"evidence-and-actions\"", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("id=\"technical-evidence\"", StringComparison.Ordinal));
        document.QuerySelector("nav[aria-label='Contents']").Should().NotBeNull();
        foreach (var link in document.QuerySelectorAll("nav[aria-label='Contents'] a"))
            document.GetElementById(link.GetAttribute("href")![1..]).Should().NotBeNull();
        document.Body!.TextContent.Should().Contain("Backend verification not recorded");
        document.Body.TextContent.Should().Contain("Configured scope");
        document.Body.TextContent.Should().NotContain("Tested scope");
    }

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

        document.QuerySelectorAll("details,article[hidden]").Should().BeEmpty("all claim content must exist in the static document");
        document.QuerySelectorAll("table").Should().NotBeEmpty();
        document.QuerySelectorAll("table").Should().OnlyContain(table => table.QuerySelectorAll("th").Length > 0);
        document.QuerySelectorAll("a[href]").All(link =>
        {
            var href = link.GetAttribute("href")!;
            if (href.StartsWith('#')) return document.GetElementById(href[1..]) != null;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }).Should().BeTrue();
        document.QuerySelectorAll("script").Should().ContainSingle();
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

        document.QuerySelectorAll("img").Should().BeEmpty();
        document.QuerySelectorAll("script").Should().ContainSingle().Subject.TextContent.Should().NotContain("alert(");
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
