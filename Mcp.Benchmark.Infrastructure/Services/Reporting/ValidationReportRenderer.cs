using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

/// <summary>
/// Renders detailed HTML and XML reports from validation results.
/// This is reusable across hosts (CLI, services, etc.).
/// </summary>
public class ValidationReportRenderer : IValidationReportRenderer
{
    public string GenerateHtmlReport(ValidationResult validationResult, ReportingConfig reportConfig, bool verbose)
    {
        var actionHints = ReportActionHintBuilder.Build(validationResult);
        var detailLabel = verbose ? "Full" : "Minimal";
        var overallStatus = validationResult.OverallStatus;
        var statusText = overallStatus.ToString();
        var statusChipClass = "status-badge status-badge--neutral";
        var statusIcon = "\u00b7"; // middle dot as neutral indicator
        reportConfig ??= new ReportingConfig();
        var coveragePercent = validationResult.Summary.CoverageRatio > 0
            ? validationResult.Summary.CoverageRatio * 100
            : (double?)null;
        var coverageCardHtml = coveragePercent.HasValue
            ? $@"                <div class=""metric-card"">
                    <div class=""metric-value"">{coveragePercent.Value:F1}%</div>
                    <div class=""metric-label"">Rule Coverage</div>
                </div>"
            : string.Empty;
        var specProfileLabel = !string.IsNullOrWhiteSpace(reportConfig.SpecProfile)
            ? reportConfig.SpecProfile
            : "latest";
        var serverProfileLabel = validationResult.ServerProfile.ToString();
        var serverProfileSource = validationResult.ServerProfileSource.ToString();
        var bootstrapHealth = ResolveBootstrapHealth(validationResult);
        var bootstrapOverviewHtml = GenerateBootstrapOverviewHtml(validationResult, bootstrapHealth);
        var protocolVersionLabel = validationResult.ProtocolVersion
            ?? validationResult.InitializationHandshake?.Payload?.ProtocolVersion
            ?? validationResult.ServerConfig.ProtocolVersion
            ?? validationResult.ValidationConfig?.Server?.ProtocolVersion
            ?? "n/a";

        if (overallStatus == ValidationStatus.Passed)
        {
            statusChipClass = "status-badge status-badge--passed";
            statusIcon = "✅"; // passed
        }
        else if (overallStatus == ValidationStatus.Failed)
        {
            statusChipClass = "status-badge status-badge--failed";
            statusIcon = "❌"; // failed
        }
        else
        {
            statusChipClass = "status-badge status-badge--warning";
            statusIcon = "ℹ️"; // informational/other
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        var htmlContent = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>MCP Server Validation Report - {detailLabel}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background-color: #f1f5f9; color: #0f172a; }}
        .container {{ max-width: 1200px; margin: 0 auto; background: white; border-radius: 12px; box-shadow: 0 15px 45px rgba(15,23,42,0.12); }}
        .header {{ background: linear-gradient(135deg, #0f172a 0%, #1d4ed8 100%); color: white; padding: 30px; border-radius: 12px 12px 0 0; }}
        .header h1 {{ margin: 0; font-size: 2.5em; }}
        .header .subtitle {{ opacity: 0.8; margin-top: 10px; }}
        .content {{ padding: 30px; }}
        .status-badge {{ display: inline-flex; align-items: center; gap: 8px; padding: 6px 14px; border-radius: 999px; font-weight: 600; font-size: 0.9rem; }}
        .status-badge--passed {{ background-color: #e6f4ea; color: #137333; border: 1px solid #c1e3cc; }}
        .status-badge--failed {{ background-color: #fce8e6; color: #b3261e; border: 1px solid #f4b4af; }}
        .status-badge--warning {{ background-color: #fff4e5; color: #8a5a00; border: 1px solid #ffddb0; }}
        .status-badge--neutral {{ background-color: #eceff1; color: #37474f; border: 1px solid #cfd8dc; }}
        .status-icon {{ width: 18px; height: 18px; border-radius: 999px; display: inline-flex; align-items: center; justify-content: center; font-size: 0.8rem; background-color: rgba(255,255,255,0.7); }}
        .metric-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }}
        .metric-card {{ border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px; background: #ffffff; box-shadow: 0 8px 24px rgba(15, 23, 42, 0.06); text-align: center; display: flex; flex-direction: column; justify-content: center; gap: 6px; }}
        .metric-card--good {{ border-color: #86efac; background: #ecfdf5; }}
        .metric-card--warn {{ border-color: #fed7aa; background: #fff7ed; }}
        .metric-card--poor {{ border-color: #fecaca; background: #fef2f2; }}
        .metric-value {{ font-size: 2rem; font-weight: 700; color: #111827; }}
        .metric-label {{ color: #6b7280; font-size: 0.9rem; text-transform: uppercase; letter-spacing: 0.05em; }}
        .capability-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 20px; margin-top: 15px; }}
        .capability-card {{ border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px; background: #fff; box-shadow: 0 8px 24px rgba(15, 23, 42, 0.08); display: flex; flex-direction: column; gap: 12px; }}
        .capability-card__header {{ display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }}
        .capability-label {{ font-size: 1rem; font-weight: 600; color: #111827; }}
        .capability-subtle {{ font-size: 0.85rem; color: #6b7280; margin-top: 2px; }}
        .capability-stats {{ display: flex; flex-wrap: wrap; gap: 16px; }}
        .stat-block {{ flex: 1 0 120px; }}
        .stat-label {{ font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; color: #6b7280; }}
        .stat-value {{ font-size: 1.35rem; font-weight: 600; color: #111827; margin-top: 4px; }}
        .capability-footnote {{ font-size: 0.9rem; color: #1f2937; }}
        .capability-footnote.subtle {{ color: #6b7280; font-size: 0.85rem; }}
        .status-pill {{ display: inline-flex; align-items: center; gap: 6px; border-radius: 999px; padding: 4px 12px; font-size: 0.78rem; font-weight: 600; border: 1px solid transparent; }}
        .status-pill--success {{ background: #ecfdf5; color: #047857; border-color: #a7f3d0; }}
        .status-pill--warn {{ background: #fff7ed; color: #c2410c; border-color: #fed7aa; }}
        .status-pill--info {{ background: #eef2ff; color: #4338ca; border-color: #c7d2fe; }}
        .insight-card {{ margin-top: 18px; padding: 18px; border-radius: 12px; border: 1px dashed #d1d5db; background: #f9fafb; color: #374151; }}
        .bootstrap-shell {{ border: 1px solid #dbeafe; border-radius: 18px; padding: 24px; background: linear-gradient(180deg, #ffffff 0%, #f8fbff 100%); box-shadow: 0 14px 36px rgba(15,23,42,0.08); }}
        .bootstrap-shell--healthy {{ border-color: #86efac; background: linear-gradient(180deg, #ffffff 0%, #f0fdf4 100%); }}
        .bootstrap-shell--protected {{ border-color: #c7d2fe; background: linear-gradient(180deg, #ffffff 0%, #eef2ff 100%); }}
        .bootstrap-shell--transient {{ border-color: #fdba74; background: linear-gradient(180deg, #ffffff 0%, #fff7ed 100%); }}
        .bootstrap-shell--inconclusive {{ border-color: #93c5fd; background: linear-gradient(180deg, #ffffff 0%, #eff6ff 100%); }}
        .bootstrap-shell--unhealthy {{ border-color: #fca5a5; background: linear-gradient(180deg, #ffffff 0%, #fef2f2 100%); }}
        .bootstrap-header {{ display: flex; justify-content: space-between; align-items: flex-start; gap: 18px; flex-wrap: wrap; }}
        .bootstrap-kicker {{ font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.08em; color: #64748b; font-weight: 700; }}
        .bootstrap-title {{ font-size: 1.55rem; font-weight: 700; color: #0f172a; margin-top: 6px; }}
        .bootstrap-copy {{ margin-top: 10px; max-width: 760px; color: #475569; line-height: 1.6; }}
        .bootstrap-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 16px; margin-top: 20px; }}
        .bootstrap-card {{ background: rgba(255,255,255,0.88); border: 1px solid rgba(148,163,184,0.22); border-radius: 14px; padding: 16px; box-shadow: 0 8px 20px rgba(15,23,42,0.06); }}
        .bootstrap-card__label {{ font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; }}
        .bootstrap-card__value {{ font-size: 1.15rem; font-weight: 700; color: #0f172a; margin-top: 8px; line-height: 1.35; }}
        .bootstrap-meta {{ display: flex; flex-wrap: wrap; gap: 8px; margin-top: 16px; }}
        .bootstrap-note {{ margin-top: 18px; border: 1px solid rgba(148,163,184,0.22); border-radius: 14px; padding: 14px 16px; background: rgba(255,255,255,0.76); color: #334155; }}
        .bootstrap-note strong {{ color: #0f172a; }}
        .section {{ margin: 30px 0; }}
        .section h2 {{ color: #333; border-bottom: 2px solid #eee; padding-bottom: 10px; }}
        .test-result {{ padding: 14px 18px; margin: 8px 0; border-left: 4px solid #ddd; background: #fafafa; }}
        .test-result.passed {{ border-left-color: #4CAF50; }}
        .test-result.failed {{ border-left-color: #F44336; }}
        .test-result.skipped {{ border-left-color: #FF9800; }}
        .error-list {{ background: #ffebee; border: 1px solid #ffcdd2; border-radius: 4px; padding: 15px; }}
        .error-item {{ margin: 5px 0; color: #c62828; }}
        .recommendation-list {{ background: #fff3e0; border: 1px solid #ffcc02; border-radius: 4px; padding: 15px; }}
        .recommendation-item {{ margin: 5px 0; color: #ef6c00; }}
        .detailed-section {{ background: #f8f9fa; border-radius: 8px; padding: 16px 18px; margin: 10px 0; }}
        .section--test-results .test-result {{
            background: transparent;
            border-left-width: 3px;
            margin: 6px 0;
            padding: 10px 16px;
        }}
        .section--test-results .detailed-section {{
            background: #f9fafb;
            padding: 10px 14px;
            margin: 8px 0 4px;
        }}
        .auth-summary {{
            background: #ffffff;
            color: #111827;
            border: 1px solid #e5e7eb;
            border-radius: 12px;
            box-shadow: 0 8px 24px rgba(15,23,42,0.06);
        }}
        .auth-summary h3 {{ color: #111827; }}
        .auth-summary .stat-label {{ color: #6b7280; }}
        .auth-summary .stat-value {{ color: #111827; }}
        .auth-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 20px; margin-top: 15px; }}
        .auth-stat {{ border: 1px solid #e5e7eb; border-radius: 10px; padding: 12px 14px; background: #f9fafb; box-shadow: 0 4px 12px rgba(15,23,42,0.04); }}
        .auth-row {{ margin-top: 12px; }}
        .meta-tags {{ display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }}
        .meta-tag {{ background: #e5e7eb; color: #111827; border: 1px solid #d1d5db; padding: 4px 10px; border-radius: 999px; font-size: 0.8rem; }}
        .finding-list {{ list-style: none; padding-left: 0; margin-top: 16px; }}
        .finding-list li {{ margin-bottom: 6px; padding-left: 20px; position: relative; font-size: 0.9rem; }}
        .finding-list li::before {{ content: ""•""; position: absolute; left: 0; color: #60a5fa; }}
        .pill {{ display: inline-flex; align-items: center; padding: 4px 12px; border-radius: 999px; font-size: 0.78rem; font-weight: 600; border: 1px solid transparent; }}
        .pill--success {{ background: #bbf7d0; color: #065f46; border-color: #86efac; }}
        .pill--warn {{ background: #fed7aa; color: #9a3412; border-color: #fdba74; }}
        .pill--info {{ background: #c7d2fe; color: #312e81; border-color: #a5b4fc; }}
        .vulnerability-card {{ background: #ffebee; border: 1px solid #ffcdd2; border-radius: 4px; padding: 10px; margin: 5px 0; }}
        .data-table {{ width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 0.9rem; }}
        .data-table th, .data-table td {{ border: 1px solid #e0e0e0; padding: 8px 10px; text-align: left; }}
        .data-table thead {{ background-color: #fafafa; }}
        .data-table tbody tr:nth-child(even) {{ background-color: #fcfcfc; }}
        .severity-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 18px; margin-top: 20px; }}
        .severity-card {{ background: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 16px; box-shadow: 0 4px 12px rgba(15,23,42,0.08); }}
        .severity-card--critical {{ border-top: 4px solid #dc2626; }}
        .severity-card--high {{ border-top: 4px solid #ea580c; }}
        .severity-card--medium {{ border-top: 4px solid #ca8a04; }}
        .severity-card--low {{ border-top: 4px solid #0d9488; }}
        .severity-card--informational {{ border-top: 4px solid #1d4ed8; }}
        .severity-label {{ font-size: 0.75rem; letter-spacing: 0.05em; text-transform: uppercase; color: #475569; }}
        .severity-value {{ font-size: 2rem; font-weight: 700; margin-top: 4px; color: #0f172a; }}
        .severity-footnote {{ font-size: 0.85rem; color: #6b7280; margin-top: 4px; }}
        .severity-badge {{ display: inline-flex; align-items: center; padding: 2px 10px; border-radius: 999px; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; }}
        .severity-badge--critical {{ background: #fee2e2; color: #b91c1c; }}
        .severity-badge--high {{ background: #ffedd5; color: #c2410c; }}
        .severity-badge--medium {{ background: #fef9c3; color: #a16207; }}
        .severity-badge--low {{ background: #ecfeff; color: #0f766e; }}
        .severity-badge--informational {{ background: #e0e7ff; color: #3730a3; }}
        .rule-table code {{ background: rgba(15,23,42,0.08); padding: 2px 6px; border-radius: 6px; }}
        .rule-secondary {{ display: block; font-size: 0.8rem; color: #6b7280; margin-top: 4px; }}
        .rule-recommendation {{ margin-top: 6px; font-size: 0.85rem; color: #155e75; }}
        .spec-placeholder {{ color: #9ca3af; font-style: italic; }}
        .footer {{ text-align: center; padding: 20px; color: #666; border-top: 1px solid #eee; }}
        pre {{ background: #f4f4f4; padding: 15px; border-radius: 4px; overflow-x: auto; }}
        .code {{ font-family: 'Courier New', monospace; background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>MCP Server Validation Report</h1>
            <div class=""subtitle"">Generated on {timestamp}</div>
        </div>
        
        <div class=""content"">
            <div class=""section"">
                <h2>Executive Summary</h2>
                <p><strong>Server:</strong> {validationResult.ServerConfig.Endpoint}</p>
                <p><strong>Validation ID:</strong> {validationResult.ValidationId}</p>
                <p><strong>Status:</strong> <span class=""{statusChipClass}""><span class=""status-icon"">{statusIcon}</span>{statusText}</span></p>
                <p><strong>Execution Time:</strong> {validationResult.StartTime:yyyy-MM-dd HH:mm:ss UTC}</p>
                <p><strong>Duration:</strong> {validationResult.Duration?.TotalSeconds:F1} seconds</p>
                <p><strong>Spec Profile:</strong> {specProfileLabel}</p>
                <p><strong>Server Profile:</strong> {serverProfileLabel} ({serverProfileSource})</p>
                <p><strong>Protocol Version:</strong> {protocolVersionLabel}</p>
            </div>

            <div class=""metric-grid"">
                <div class=""metric-card"">
                    <div class=""metric-value"">{validationResult.ComplianceScore:F1}%</div>
                    <div class=""metric-label"">Compliance Score</div>
                </div>
                <div class=""metric-card"">
                    <div class=""metric-value"">{validationResult.Summary.TotalTests}</div>
                    <div class=""metric-label"">Total Tests</div>
                </div>
                <div class=""metric-card"">
                    <div class=""metric-value"">{validationResult.Summary.PassedTests}</div>
                    <div class=""metric-label"">Passed Tests</div>
                </div>
                <div class=""metric-card"">
                    <div class=""metric-value"">{validationResult.Summary.FailedTests}</div>
                    <div class=""metric-label"">Failed Tests</div>
                </div>
                <div class=""metric-card"">
                    <div class=""metric-value"">{validationResult.Summary.PassRate:F1}%</div>
                    <div class=""metric-label"">Pass Rate</div>
                </div>
                {coverageCardHtml}
            </div>

            {bootstrapOverviewHtml}

            {(validationResult.CriticalErrors.Count > 0 ? $@"
            <div class=""section"">
                <h2>Critical Errors</h2>
                <div class=""error-list"">
                    {string.Join("", validationResult.CriticalErrors.Select(error => $"<div class=\"error-item\">• {error}</div>"))}
                </div>
            </div>" : "")}

            {(actionHints.Count > 0 ? $@"
            <div class=""section"">
                <h2>Action Hints</h2>
                <div class=""recommendation-list"">
                    {string.Join("", actionHints.Select(hint => $"<div class=\"recommendation-item\">• {System.Net.WebUtility.HtmlEncode(hint)}</div>"))}
                </div>
            </div>" : "")}

            {(validationResult.Recommendations.Count > 0 ? $@"
            <div class=""section"">
                <h2>Recommendations</h2>
                <div class=""recommendation-list"">
                    {string.Join("", validationResult.Recommendations.Select(rec => $"<div class=\"recommendation-item\">• {rec}</div>"))}
                </div>
            </div>" : "")}

            {GenerateCapabilitySnapshotHtml(validationResult)}{GenerateClientCompatibilityHtml(validationResult)}

            <div class=""section section--test-results"">
                <h2>Test Results Summary</h2>
                {GenerateTestResultsSummaryHtml(validationResult, verbose)}
            </div>

            {GenerateRuleInsightsHtml(validationResult, reportConfig)}
            {GenerateSecurityDetailsHtml(validationResult, verbose)}
            {GenerateToolDetailsHtml(validationResult, verbose)}
            {GenerateResourceDetailsHtml(validationResult, verbose)}
            {GeneratePerformanceDetailsHtml(validationResult, verbose)}
        </div>

        <div class=""footer"">
            Generated by MCP Benchmark for MCP Servers - {detailLabel} report
        </div>
    </div>
</body>
</html>";

        return htmlContent;
    }

    public string GenerateXmlReport(ValidationResult validationResult, bool verbose)
    {
        var reportElement = new XElement("ValidationReport",
            new XElement("Report",
                new XElement("Title", "MCP Server Validation Report"),
                new XElement("GeneratedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("ValidationId", validationResult.ValidationId)
            ),
            new XElement("Server",
                new XElement("Endpoint", validationResult.ServerConfig.Endpoint),
                new XElement("Transport", validationResult.ServerConfig.Transport)
            ),
            new XElement("Results",
                new XElement("OverallStatus", validationResult.OverallStatus.ToString()),
                new XElement("ComplianceScore", validationResult.ComplianceScore.ToString("F1")),
                new XElement("DurationSeconds", (validationResult.Duration?.TotalSeconds ?? 0).ToString("F1")),
                new XElement("StartTime", validationResult.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("EndTime", validationResult.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            ),
            new XElement("Summary",
                new XElement("TotalTests", validationResult.Summary.TotalTests),
                new XElement("PassedTests", validationResult.Summary.PassedTests),
                new XElement("FailedTests", validationResult.Summary.FailedTests),
                new XElement("SkippedTests", validationResult.Summary.SkippedTests),
                new XElement("PassRate", validationResult.Summary.PassRate.ToString("F1")),
                new XElement("CriticalIssues", validationResult.Summary.CriticalIssues),
                new XElement("Warnings", validationResult.Summary.Warnings),
                new XElement("CoverageRatio", validationResult.Summary.CoverageRatio.ToString("F3"))
            )
        );

        var specProfile = validationResult.ValidationConfig?.Reporting?.SpecProfile ?? "latest";
        reportElement.Add(new XElement("Profiles",
            new XElement("ServerProfile", validationResult.ServerProfile.ToString()),
            new XElement("ServerProfileSource", validationResult.ServerProfileSource.ToString()),
            new XElement("SpecProfile", specProfile)
        ));

        var bootstrapElement = BuildBootstrapHealthElement(validationResult);
        if (bootstrapElement != null)
        {
            reportElement.Add(bootstrapElement);
        }

        var severityBreakdown = BuildSeverityBreakdownElement(validationResult);
        if (severityBreakdown != null)
        {
            reportElement.Add(severityBreakdown);
        }

        if (validationResult.CapabilitySnapshot?.Payload is CapabilitySummary snapshot)
        {
            reportElement.Add(BuildCapabilitySnapshotElement(snapshot));
        }

        var clientCompatibilityElement = BuildClientCompatibilityElement(validationResult);
        if (clientCompatibilityElement != null)
        {
            reportElement.Add(clientCompatibilityElement);
        }

        var testCategories = new XElement("TestCategories");

        if (validationResult.ProtocolCompliance != null)
        {
            var protocol = validationResult.ProtocolCompliance;
            var protocolElement = new XElement("ProtocolCompliance",
                new XAttribute("status", protocol.Status.ToString()),
                new XAttribute("score", protocol.ComplianceScore.ToString("F1")),
                new XAttribute("durationSeconds", protocol.Duration.TotalSeconds.ToString("F1")),
                new XElement("ViolationsCount", protocol.Violations.Count)
            );

            if (verbose && protocol.Violations.Any())
            {
                var violationsElement = new XElement("Violations",
                    protocol.Violations.Select(v =>
                    {
                        var violationElement = new XElement("Violation",
                            new XAttribute("severity", v.Severity.ToString()),
                            new XAttribute("source", ValidationRuleSourceClassifier.GetLabel(v)),
                            new XAttribute("category", v.Category ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.CheckId))
                        {
                            violationElement.Add(new XAttribute("checkId", v.CheckId));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Rule))
                        {
                            violationElement.Add(new XAttribute("rule", v.Rule));
                        }

                        violationElement.Add(new XElement("Description", v.Description ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.SpecReference))
                        {
                            violationElement.Add(new XElement("SpecReference", v.SpecReference));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Recommendation))
                        {
                            violationElement.Add(new XElement("Recommendation", v.Recommendation));
                        }

                        if (v.Context?.Any() == true)
                        {
                            violationElement.Add(new XElement("Context",
                                v.Context.Select(kvp => new XElement("Item",
                                    new XAttribute("key", kvp.Key ?? string.Empty),
                                    new XAttribute("value", kvp.Value?.ToString() ?? string.Empty)))));
                        }

                        return violationElement;
                    }));
                protocolElement.Add(violationsElement);
            }

            testCategories.Add(protocolElement);
        }

        if (validationResult.SecurityTesting != null)
        {
            var security = validationResult.SecurityTesting;
            var securityElement = new XElement("SecurityTesting",
                new XAttribute("status", security.Status.ToString()),
                new XAttribute("securityScore", security.SecurityScore.ToString("F1")),
                new XAttribute("durationSeconds", security.Duration.TotalSeconds.ToString("F1")),
                new XElement("VulnerabilitiesCount", security.Vulnerabilities.Count)
            );

            if (verbose && security.Vulnerabilities.Any())
            {
                var vulnerabilitiesElement = new XElement("Vulnerabilities",
                    security.Vulnerabilities.Select(v =>
                    {
                        var vulnerabilityElement = new XElement("Vulnerability",
                            new XAttribute("severity", v.Severity.ToString()),
                            new XAttribute("source", ValidationRuleSourceClassifier.GetLabel(v)),
                            new XAttribute("category", v.Category ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.Id))
                        {
                            vulnerabilityElement.Add(new XAttribute("id", v.Id));
                        }

                        vulnerabilityElement.Add(new XElement("Name", v.Name ?? string.Empty));
                        vulnerabilityElement.Add(new XElement("Description", v.Description ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.AffectedComponent))
                        {
                            vulnerabilityElement.Add(new XElement("AffectedComponent", v.AffectedComponent));
                        }

                        if (v.CvssScore.HasValue)
                        {
                            vulnerabilityElement.Add(new XElement("CvssScore", v.CvssScore.Value.ToString("F1")));
                        }

                        vulnerabilityElement.Add(new XElement("IsExploitable", v.IsExploitable));

                        if (!string.IsNullOrWhiteSpace(v.ProofOfConcept))
                        {
                            vulnerabilityElement.Add(new XElement("ProofOfConcept", v.ProofOfConcept));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Remediation))
                        {
                            vulnerabilityElement.Add(new XElement("Remediation", v.Remediation));
                        }

                        return vulnerabilityElement;
                    }));
                securityElement.Add(vulnerabilitiesElement);
            }

            if (verbose && security.AuthenticationTestResult?.TestScenarios?.Any() == true)
            {
                var authElement = new XElement("AuthenticationScenarios",
                    security.AuthenticationTestResult.TestScenarios.Select(s =>
                        new XElement("Scenario",
                            new XAttribute("method", s.Method ?? string.Empty),
                            new XElement("ScenarioName", s.ScenarioName ?? string.Empty),
                            new XElement("ExpectedBehavior", s.ExpectedBehavior ?? string.Empty),
                            new XElement("ActualBehavior", s.ActualBehavior ?? string.Empty),
                            new XElement("Analysis", s.Analysis ?? string.Empty))));
                securityElement.Add(authElement);
            }

            if (verbose && security.AttackSimulations?.Any() == true)
            {
                var attacksElement = new XElement("AttackSimulations",
                    security.AttackSimulations.Select(a =>
                        new XElement("Simulation",
                            new XAttribute("defenseSuccessful", a.DefenseSuccessful),
                            new XElement("AttackVector", a.AttackVector ?? string.Empty),
                            new XElement("Description", a.Description ?? string.Empty),
                            new XElement("ServerResponse", a.ServerResponse ?? string.Empty))));
                securityElement.Add(attacksElement);
            }

            testCategories.Add(securityElement);
        }

        if (validationResult.ToolValidation != null)
        {
            var tools = validationResult.ToolValidation;
            var toolsElement = new XElement("ToolValidation",
                new XAttribute("status", tools.Status.ToString()),
                new XAttribute("durationSeconds", tools.Duration.TotalSeconds.ToString("F1")),
                new XElement("ToolsDiscovered", tools.ToolsDiscovered),
                new XElement("ToolsPassed", tools.ToolsTestPassed),
                new XElement("ToolsFailed", tools.ToolsTestFailed)
            );

            if (verbose && tools.ToolResults?.Any() == true)
            {
                var toolResultsElement = new XElement("ToolResults",
                    tools.ToolResults.Select(t =>
                        new XElement("ToolResult",
                            new XElement("ToolName", t.ToolName ?? string.Empty),
                            new XElement("Status", t.Status.ToString()),
                            new XElement("ExecutionTimeMs", t.ExecutionTimeMs.ToString("F2")))));
                toolsElement.Add(toolResultsElement);
            }

            if (tools.AuthenticationSecurity != null)
            {
                var auth = tools.AuthenticationSecurity;
                var authElement = new XElement("AuthenticationSecurity",
                    new XAttribute("enforced", tools.AuthenticationProperlyEnforced),
                    new XElement("AuthenticationRequired", auth.AuthenticationRequired),
                    new XElement("RejectsUnauthenticated", auth.RejectsUnauthenticated),
                    new XElement("HasProperAuthHeaders", auth.HasProperAuthHeaders),
                    new XElement("SecurityScore", auth.SecurityScore.ToString("F1")));

                if (auth.ChallengeStatusCode.HasValue)
                {
                    authElement.Add(new XElement("ChallengeStatusCode", auth.ChallengeStatusCode.Value));
                }

                if (auth.ChallengeDurationMs > 0)
                {
                    authElement.Add(new XElement("ChallengeDurationMs", auth.ChallengeDurationMs.ToString("F1")));
                }

                if (!string.IsNullOrWhiteSpace(auth.WwwAuthenticateHeader))
                {
                    authElement.Add(new XElement("WwwAuthenticate", auth.WwwAuthenticateHeader));
                }

                if (auth.AuthMetadata != null)
                {
                    var metadataElement = new XElement("AuthMetadata");
                    if (!string.IsNullOrWhiteSpace(auth.AuthMetadata.Resource))
                    {
                        metadataElement.Add(new XElement("Resource", auth.AuthMetadata.Resource));
                    }
                    if (auth.AuthMetadata.AuthorizationServers?.Any() == true)
                    {
                        metadataElement.Add(new XElement("AuthorizationServers",
                            auth.AuthMetadata.AuthorizationServers.Select(s => new XElement("Server", s))));
                    }
                    if (auth.AuthMetadata.ScopesSupported?.Any() == true)
                    {
                        metadataElement.Add(new XElement("ScopesSupported",
                            auth.AuthMetadata.ScopesSupported.Select(s => new XElement("Scope", s))));
                    }
                    if (auth.AuthMetadata.BearerMethodsSupported?.Any() == true)
                    {
                        metadataElement.Add(new XElement("BearerMethodsSupported",
                            auth.AuthMetadata.BearerMethodsSupported.Select(s => new XElement("Method", s))));
                    }
                    authElement.Add(metadataElement);
                }

                if (auth.Findings.Any())
                {
                    authElement.Add(new XElement("Findings", auth.Findings.Select(f => new XElement("Finding", f))));
                }

                toolsElement.Add(authElement);
            }

            testCategories.Add(toolsElement);
        }

        if (validationResult.PerformanceTesting != null)
        {
            var perf = validationResult.PerformanceTesting;
            var hasObservedPerformanceMetrics = PerformanceMeasurementEvaluator.HasObservedMetrics(perf);
            var perfElement = new XElement("PerformanceTesting",
                new XAttribute("status", perf.Status.ToString()),
                new XAttribute("durationSeconds", perf.Duration.TotalSeconds.ToString("F1"))
            );

            if (verbose && hasObservedPerformanceMetrics)
            {
                var loadElement = new XElement("LoadTesting",
                    new XElement("AverageLatencyMs", perf.LoadTesting.AverageResponseTimeMs.ToString("F2")),
                    new XElement("P95LatencyMs", perf.LoadTesting.P95ResponseTimeMs.ToString("F2")),
                    new XElement("RequestsPerSecond", perf.LoadTesting.RequestsPerSecond.ToString("F2")),
                    new XElement("ErrorRate", perf.LoadTesting.ErrorRate.ToString("F2"))
                );
                perfElement.Add(loadElement);
            }
            else if (verbose)
            {
                perfElement.Add(
                    new XElement("MeasurementStatus", "Unavailable"),
                    new XElement(
                        "Reason",
                        PerformanceMeasurementEvaluator.GetUnavailableReason(
                            perf,
                            "Performance measurements were not captured before the run ended.")));
            }

            testCategories.Add(perfElement);
        }

        if (validationResult.ResourceTesting != null)
        {
            var resources = validationResult.ResourceTesting;
            var resourcesElement = new XElement("ResourceTesting",
                new XAttribute("status", resources.Status.ToString()),
                new XAttribute("durationSeconds", resources.Duration.TotalSeconds.ToString("F1")),
                new XElement("ResourcesDiscovered", resources.ResourcesDiscovered),
                new XElement("ResourcesAccessible", resources.ResourcesAccessible)
            );

            if (verbose && resources.ResourceResults?.Any() == true)
            {
                var resourceResultsElement = new XElement("ResourceResults",
                    resources.ResourceResults.Select(r =>
                        new XElement("Resource",
                            new XElement("ResourceName", r.ResourceName ?? string.Empty),
                            new XElement("ResourceUri", r.ResourceUri ?? string.Empty),
                            new XElement("MimeType", r.MimeType ?? string.Empty),
                            new XElement("ContentSize", r.ContentSize?.ToString() ?? string.Empty),
                            new XElement("Status", r.Status.ToString()))));
                resourcesElement.Add(resourceResultsElement);
            }

            testCategories.Add(resourcesElement);
        }

        if (validationResult.PromptTesting != null)
        {
            var prompts = validationResult.PromptTesting;
            var promptsElement = new XElement("PromptTesting",
                new XAttribute("status", prompts.Status.ToString()),
                new XAttribute("durationSeconds", prompts.Duration.TotalSeconds.ToString("F1")),
                new XElement("PromptsDiscovered", prompts.PromptsDiscovered),
                new XElement("PromptsPassed", prompts.PromptsTestPassed)
            );

            testCategories.Add(promptsElement);
        }

        reportElement.Add(testCategories);

        var issuesElement = new XElement("Issues");

        if (validationResult.CriticalErrors.Any())
        {
            var errorsElement = new XElement("CriticalErrors",
                validationResult.CriticalErrors.Select(e => new XElement("Error", e)));
            issuesElement.Add(errorsElement);
        }

        if (validationResult.Recommendations.Any())
        {
            var recommendationsElement = new XElement("Recommendations",
                validationResult.Recommendations.Select(r => new XElement("Recommendation", r)));
            issuesElement.Add(recommendationsElement);
        }

        reportElement.Add(issuesElement);

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), reportElement);
        return document.ToString();
    }

    public string GenerateSarifReport(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var entries = BuildSarifEntries(validationResult)
            .GroupBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var rules = entries
            .GroupBy(entry => entry.RuleId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    id = first.RuleId,
                    name = first.RuleId,
                    shortDescription = new { text = first.ShortDescription },
                    fullDescription = new { text = first.FullDescription },
                    help = string.IsNullOrWhiteSpace(first.Recommendation)
                        ? null
                        : new { text = first.Recommendation },
                    helpUri = first.HelpUri,
                    properties = new
                    {
                        source = first.Source,
                        category = first.Category,
                        tags = BuildSarifTags(first.Source, first.Category, first.Component)
                    }
                };
            })
            .ToList();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "MCP Benchmark",
                    fullName = "MCP Benchmark Validation Suite",
                    semanticVersion = "1.0.0",
                    informationUri = "https://github.com/navalerakesh/mcp-validation-security",
                    rules
                }
            },
            automationDetails = new
            {
                id = validationResult.ValidationId,
                description = new { text = "MCP validation run" }
            },
            invocations = new[]
            {
                new
                {
                    executionSuccessful = validationResult.OverallStatus != ValidationStatus.InProgress,
                    startTimeUtc = validationResult.StartTime.ToUniversalTime().ToString("O"),
                    endTimeUtc = validationResult.EndTime?.ToUniversalTime().ToString("O"),
                    properties = new
                    {
                        validationId = validationResult.ValidationId,
                        endpoint = validationResult.ServerConfig.Endpoint,
                        transport = validationResult.ServerConfig.Transport,
                        overallStatus = validationResult.OverallStatus.ToString(),
                        serverProfile = validationResult.ServerProfile.ToString(),
                        specProfile = validationResult.ValidationConfig.Reporting.SpecProfile
                    }
                }
            },
            results = entries.Select(entry => new
            {
                ruleId = entry.RuleId,
                level = entry.Level,
                kind = "fail",
                message = new { text = entry.Message },
                partialFingerprints = new { logicalIdentity = entry.Fingerprint },
                locations = string.IsNullOrWhiteSpace(entry.Component)
                    ? null
                    : new[]
                    {
                        new
                        {
                            logicalLocations = new[]
                            {
                                new
                                {
                                    kind = "component",
                                    name = entry.Component
                                }
                            }
                        }
                    },
                properties = entry.Properties
            }).ToList()
        };

        var sarif = new
        {
            version = "2.1.0",
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[] { run }
        };

        return JsonSerializer.Serialize(sarif, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }).Replace("\"schema\"", "\"$schema\"");
    }

    public string GenerateJunitReport(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var testCases = BuildJunitTestCases(validationResult);
        var suiteTimestamp = (validationResult.StartTime == default ? DateTime.UtcNow : validationResult.StartTime)
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

        var suites = testCases
            .GroupBy(testCase => testCase.SuiteName, StringComparer.Ordinal)
            .Select(group =>
            {
                var tests = group.Count();
                var failures = group.Count(testCase => testCase.Outcome == JunitOutcome.Failure);
                var errors = group.Count(testCase => testCase.Outcome == JunitOutcome.Error);
                var skipped = group.Count(testCase => testCase.Outcome == JunitOutcome.Skipped);
                var suite = new XElement("testsuite",
                    new XAttribute("name", group.Key),
                    new XAttribute("tests", tests),
                    new XAttribute("failures", failures),
                    new XAttribute("errors", errors),
                    new XAttribute("skipped", skipped),
                    new XAttribute("time", group.Sum(testCase => testCase.TimeSeconds).ToString("F3", CultureInfo.InvariantCulture)),
                    new XAttribute("timestamp", suiteTimestamp));

                suite.Add(new XElement("properties",
                    new XElement("property", new XAttribute("name", "validationId"), new XAttribute("value", validationResult.ValidationId)),
                    new XElement("property", new XAttribute("name", "endpoint"), new XAttribute("value", validationResult.ServerConfig.Endpoint ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "transport"), new XAttribute("value", validationResult.ServerConfig.Transport ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "overallStatus"), new XAttribute("value", validationResult.OverallStatus.ToString())),
                    new XElement("property", new XAttribute("name", "policyMode"), new XAttribute("value", validationResult.PolicyOutcome?.Mode ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "specProfile"), new XAttribute("value", validationResult.ValidationConfig?.Reporting?.SpecProfile ?? "latest"))));

                foreach (var testCase in group)
                {
                    var testCaseElement = new XElement("testcase",
                        new XAttribute("classname", testCase.ClassName),
                        new XAttribute("name", testCase.Name),
                        new XAttribute("time", testCase.TimeSeconds.ToString("F3", CultureInfo.InvariantCulture)));

                    switch (testCase.Outcome)
                    {
                        case JunitOutcome.Failure:
                            testCaseElement.Add(new XElement("failure",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                        case JunitOutcome.Error:
                            testCaseElement.Add(new XElement("error",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                        case JunitOutcome.Skipped:
                            testCaseElement.Add(new XElement("skipped",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                    }

                    if (!string.IsNullOrWhiteSpace(testCase.SystemOut))
                    {
                        testCaseElement.Add(new XElement("system-out", testCase.SystemOut));
                    }

                    suite.Add(testCaseElement);
                }

                return suite;
            })
            .ToList();

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement("testsuites",
                new XAttribute("name", "MCP Validator"),
                new XAttribute("tests", testCases.Count),
                new XAttribute("failures", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Failure)),
                new XAttribute("errors", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Error)),
                new XAttribute("skipped", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Skipped)),
                new XAttribute("time", testCases.Sum(testCase => testCase.TimeSeconds).ToString("F3", CultureInfo.InvariantCulture)),
                suites));

        return document.ToString();
    }

    private string GenerateTestResultsSummaryHtml(ValidationResult validationResult, bool verbose)
    {
        var html = new StringBuilder();

        if (validationResult.ProtocolCompliance != null)
        {
            var statusClass = validationResult.ProtocolCompliance.Status switch
            {
                TestStatus.Passed => "passed",
                TestStatus.Skipped => "skipped",
                _ => "failed"
            };

            html.AppendLine($"<div class=\"test-result {statusClass}\">");
            html.AppendLine($"                <strong>Protocol Compliance</strong> - {validationResult.ProtocolCompliance.Status}<br>");
            html.AppendLine($"                Score: {validationResult.ProtocolCompliance.ComplianceScore:F1}% | Duration: {validationResult.ProtocolCompliance.Duration.TotalSeconds:F1}s");

            if (verbose && validationResult.ProtocolCompliance.Violations?.Any() == true)
            {
                html.AppendLine("<div class=\"detailed-section\">");
                html.AppendLine($"                    <h4>Protocol Violations ({validationResult.ProtocolCompliance.Violations.Count})</h4>");
                foreach (var violation in validationResult.ProtocolCompliance.Violations)
                {
                    html.AppendLine($"<div class=\"error-item\">• {violation.Description}</div>");
                }
                html.AppendLine("</div>");
            }

            html.AppendLine("</div>");
        }

        if (validationResult.ToolValidation != null)
        {
            var statusClass = validationResult.ToolValidation.Status switch
            {
                TestStatus.Passed => "passed",
                TestStatus.Skipped => "skipped",
                _ => "failed"
            };

            html.AppendLine($"<div class=\"test-result {statusClass}\">");
            html.AppendLine($"                <strong>Tool Validation</strong> - {validationResult.ToolValidation.Status}<br>");
            html.AppendLine($"                Tools Discovered: {validationResult.ToolValidation.ToolsDiscovered} | Passed: {validationResult.ToolValidation.ToolsTestPassed} | Failed: {validationResult.ToolValidation.ToolsTestFailed}");

            if (verbose && validationResult.ToolValidation.ToolResults?.Any() == true)
            {
                html.AppendLine("<div class=\"detailed-section\">");
                html.AppendLine("                    <h4>Tool Test Results</h4>");
                foreach (var tool in validationResult.ToolValidation.ToolResults)
                {
                    var toolStatusIcon = tool.Status switch
                    {
                        TestStatus.Passed => "✅",
                        TestStatus.Failed => "❌",
                        TestStatus.Skipped => "➖",
                        _ => "ℹ️"
                    };
                    var safeName = System.Net.WebUtility.HtmlEncode(tool.ToolName);
                    html.AppendLine($"<div>{toolStatusIcon} <span class=\"code\">{safeName}</span> - {tool.Status}</div>");
                }
                html.AppendLine("                </div>");
            }

            html.AppendLine("</div>");
        }

        if (validationResult.SecurityTesting != null)
        {
            var statusClass = validationResult.SecurityTesting.Status switch
            {
                TestStatus.Passed => "passed",
                TestStatus.Skipped => "skipped",
                _ => "failed"
            };

            html.AppendLine($"<div class=\"test-result {statusClass}\">");
            html.AppendLine($"                <strong>Security Testing</strong> - {validationResult.SecurityTesting.Status}<br>");
            html.AppendLine($"                Security Score: {validationResult.SecurityTesting.SecurityScore:F1}% | Vulnerabilities: {validationResult.SecurityTesting.Vulnerabilities.Count}");

            if (verbose && validationResult.SecurityTesting.Vulnerabilities?.Any() == true)
            {
                html.AppendLine("<div class=\"detailed-section\">");
                html.AppendLine($"                    <h4>Security Vulnerabilities ({validationResult.SecurityTesting.Vulnerabilities.Count})</h4>");
                foreach (var vuln in validationResult.SecurityTesting.Vulnerabilities)
                {
                    html.AppendLine("<div class=\"vulnerability-card\">");
                    html.AppendLine($"                        <strong>{vuln.Name}</strong> ({vuln.Severity})<br>");
                    html.AppendLine($"                        <small>{vuln.Description}</small>");
                    html.AppendLine("                    </div>");
                }
                html.AppendLine("                </div>");
            }

            html.AppendLine("</div>");
        }

        if (validationResult.PerformanceTesting != null)
        {
            var statusClass = validationResult.PerformanceTesting.Status switch
            {
                TestStatus.Passed => "passed",
                TestStatus.Skipped => "skipped",
                _ => "failed"
            };

            html.AppendLine($"<div class=\"test-result {statusClass}\">");
            html.AppendLine($"                <strong>Performance Testing</strong> - {validationResult.PerformanceTesting.Status}<br>");
            html.AppendLine($"                Duration: {validationResult.PerformanceTesting.Duration.TotalSeconds:F1}s");

            if (verbose)
            {
                var hasObservedPerformanceMetrics = PerformanceMeasurementEvaluator.HasObservedMetrics(validationResult.PerformanceTesting);
                html.AppendLine("<div class=\"detailed-section\">");
                html.AppendLine($"                    <h4>Performance Metrics</h4>");
                html.AppendLine($"                    <p>Test Duration: {validationResult.PerformanceTesting.Duration.TotalSeconds:F1} seconds</p>");
                html.AppendLine($"                    <p>Status: {validationResult.PerformanceTesting.Status}</p>");

                if (!hasObservedPerformanceMetrics)
                {
                    var unavailableReason = PerformanceMeasurementEvaluator.GetUnavailableReason(
                        validationResult.PerformanceTesting,
                        "Performance measurements were not captured before the run ended.");
                    html.AppendLine("                    <p><strong>Measurements:</strong> Unavailable</p>");
                    html.AppendLine($"                    <p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(unavailableReason)}</p>");
                }

                html.AppendLine("                </div>");
            }

            html.AppendLine("</div>");
        }

        return html.ToString();
    }

    private string GenerateCapabilitySnapshotHtml(ValidationResult validationResult)
    {
        if (validationResult.CapabilitySnapshot?.Payload is not CapabilitySummary snapshot)
        {
            return string.Empty;
        }

        var html = new StringBuilder();
        html.AppendLine("<div class=\"section\">");
        html.AppendLine("  <h2>Capability Snapshot</h2>");
        html.AppendLine("  <div class=\"capability-grid\">");

        html.Append(RenderProbeCard(
            "Tools/list",
            snapshot.DiscoveredToolsCount,
            snapshot.ToolListResponse?.StatusCode,
            snapshot.ToolListDurationMs,
            snapshot.ToolListingSucceeded,
            FormatProbeResultHtml(snapshot.ToolListingSucceeded, "Listed", "Failed"),
            BuildToolFootnote()));

        html.Append(RenderProbeCard(
            "Resources/list",
            snapshot.DiscoveredResourcesCount,
            snapshot.ResourceListResponse?.StatusCode,
            snapshot.ResourceListDurationMs,
            snapshot.ResourceListingSucceeded,
            FormatProbeResultHtml(snapshot.ResourceListingSucceeded, "Listed", "Failed")));

        html.Append(RenderProbeCard(
            "Prompts/list",
            snapshot.DiscoveredPromptsCount,
            snapshot.PromptListResponse?.StatusCode,
            snapshot.PromptListDurationMs,
            snapshot.PromptListingSucceeded,
            FormatProbeResultHtml(snapshot.PromptListingSucceeded, "Listed", "Failed")));

        html.AppendLine("  </div>");

        if (!string.IsNullOrWhiteSpace(snapshot.FirstToolName))
        {
            var toolName = System.Net.WebUtility.HtmlEncode(snapshot.FirstToolName);
            var invocationState = snapshot.ToolInvocationSucceeded ? "responded successfully." : "could not be invoked.";
            html.AppendLine("  <div class=\"insight-card\">");
            html.AppendLine("    <div class=\"stat-label\">Primary tool validation</div>");
            html.AppendLine($"    <div class=\"capability-footnote\"><code>{toolName}</code> {invocationState}</div>");
            html.AppendLine("  </div>");
        }

        html.AppendLine("</div>");
        return html.ToString();

        string RenderProbeCard(string title, int discovered, int? statusCode, double durationMs, bool succeeded, string resultText, string? footnote = null)
        {
            var statusClass = succeeded ? "status-pill status-pill--success" : "status-pill status-pill--warn";
            var statusLabel = succeeded ? "Healthy" : "Attention";
            var encodedTitle = System.Net.WebUtility.HtmlEncode(title);
            var httpStatus = System.Net.WebUtility.HtmlEncode(FormatHttpStatus(statusCode));
            var builder = new StringBuilder();
            builder.AppendLine("    <div class=\"capability-card\">");
            builder.AppendLine("      <div class=\"capability-card__header\">");
            builder.AppendLine("        <div>");
            builder.AppendLine($"          <div class=\"capability-label\">{encodedTitle}</div>");
            builder.AppendLine($"          <div class=\"capability-subtle\">{httpStatus}</div>");
            builder.AppendLine("        </div>");
            builder.AppendLine($"        <span class=\"{statusClass}\">{statusLabel}</span>");
            builder.AppendLine("      </div>");
            builder.AppendLine("      <div class=\"capability-stats\">");
            builder.AppendLine(RenderStatBlock("Discovered", discovered.ToString()));
            builder.AppendLine(RenderStatBlock("Duration", FormatDuration(durationMs)));
            builder.AppendLine("      </div>");
            builder.AppendLine($"      <div class=\"capability-footnote\">{resultText}</div>");
            if (!string.IsNullOrWhiteSpace(footnote))
            {
                builder.AppendLine($"      <div class=\"capability-footnote subtle\">{footnote}</div>");
            }
            builder.AppendLine("    </div>");
            return builder.ToString();
        }

        string RenderStatBlock(string label, string value)
        {
            var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
            var encodedValue = System.Net.WebUtility.HtmlEncode(value);
            return $"        <div class=\"stat-block\"><div class=\"stat-label\">{encodedLabel}</div><div class=\"stat-value\">{encodedValue}</div></div>";
        }

        string? BuildToolFootnote()
        {
            if (string.IsNullOrWhiteSpace(snapshot.FirstToolName))
            {
                return null;
            }

            var encodedTool = System.Net.WebUtility.HtmlEncode(snapshot.FirstToolName);
            var invocationIcon = snapshot.ToolInvocationSucceeded ? "✅" : "⚠️";
            var invocationLabel = snapshot.ToolInvocationSucceeded ? "Invocation verified" : "Invocation blocked";
            return $"{invocationIcon} Primary tool <code>{encodedTool}</code> · {invocationLabel}";
        }

    }

    private string GenerateClientCompatibilityHtml(ValidationResult validationResult)
    {
        if (validationResult.ClientCompatibility?.Assessments.Count > 0 != true)
        {
            return string.Empty;
        }

        var html = new StringBuilder();
        html.AppendLine("<div class=\"section\">");
        html.AppendLine("  <h2>Client Compatibility</h2>");
        html.AppendLine("  <div class=\"capability-grid\">");

        foreach (var assessment in validationResult.ClientCompatibility.Assessments)
        {
            var statusClass = assessment.Status switch
            {
                ClientProfileCompatibilityStatus.Compatible => "status-pill status-pill--success",
                ClientProfileCompatibilityStatus.CompatibleWithWarnings => "status-pill status-pill--warn",
                ClientProfileCompatibilityStatus.Incompatible => "status-pill status-pill--warn",
                _ => "status-pill status-pill--info"
            };
            var statusLabel = System.Net.WebUtility.HtmlEncode(assessment.StatusLabel);
            var title = System.Net.WebUtility.HtmlEncode(assessment.DisplayName);
            var summary = System.Net.WebUtility.HtmlEncode(assessment.Summary);

            html.AppendLine("    <div class=\"capability-card\">");
            html.AppendLine("      <div class=\"capability-card__header\">");
            html.AppendLine("        <div>");
            html.AppendLine($"          <div class=\"capability-label\">{title}</div>");
            html.AppendLine($"          <div class=\"capability-subtle\">{System.Net.WebUtility.HtmlEncode(assessment.ProfileId)} · {System.Net.WebUtility.HtmlEncode(assessment.Revision)}</div>");
            html.AppendLine("        </div>");
            html.AppendLine($"        <span class=\"{statusClass}\">{statusLabel}</span>");
            html.AppendLine("      </div>");
            html.AppendLine("      <div class=\"capability-stats\">");
            html.AppendLine(RenderStatBlock("Passed", assessment.PassedRequirements.ToString(CultureInfo.InvariantCulture)));
            html.AppendLine(RenderStatBlock("Warnings", assessment.WarningRequirements.ToString(CultureInfo.InvariantCulture)));
            html.AppendLine(RenderStatBlock("Failed", assessment.FailedRequirements.ToString(CultureInfo.InvariantCulture)));
            html.AppendLine("      </div>");
            html.AppendLine($"      <div class=\"capability-footnote\">{summary}</div>");

            if (!string.IsNullOrWhiteSpace(assessment.DocumentationUrl))
            {
                var docUrl = System.Net.WebUtility.HtmlEncode(assessment.DocumentationUrl);
                html.AppendLine($"      <div class=\"capability-footnote subtle\"><a href=\"{docUrl}\" target=\"_blank\" rel=\"noreferrer\">Profile documentation</a></div>");
            }

            var noteworthyRequirements = assessment.Requirements
                .Where(requirement => requirement.Outcome is ClientProfileRequirementOutcome.Warning or ClientProfileRequirementOutcome.Failed)
                .Take(3)
                .ToList();

            if (noteworthyRequirements.Count > 0)
            {
                html.AppendLine("      <ul class=\"finding-list\">");
                foreach (var requirement in noteworthyRequirements)
                {
                    var detail = new StringBuilder(requirement.Summary);
                    if (requirement.ExampleComponents.Count > 0)
                    {
                        detail.Append($" Examples: {string.Join(", ", requirement.ExampleComponents)}.");
                    }

                    if (requirement.RuleIds.Count > 0)
                    {
                        detail.Append($" Rule IDs: {string.Join(", ", requirement.RuleIds)}.");
                    }

                    html.AppendLine($"        <li><strong>{System.Net.WebUtility.HtmlEncode(requirement.Title)}</strong>: {System.Net.WebUtility.HtmlEncode(detail.ToString())}</li>");
                }

                html.AppendLine("      </ul>");
            }

            html.AppendLine("    </div>");
        }

        html.AppendLine("  </div>");
        html.AppendLine("</div>");
        return html.ToString();

        string RenderStatBlock(string label, string value)
        {
            var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
            var encodedValue = System.Net.WebUtility.HtmlEncode(value);
            return $"        <div class=\"stat-block\"><div class=\"stat-label\">{encodedLabel}</div><div class=\"stat-value\">{encodedValue}</div></div>";
        }
    }

    private string GenerateBootstrapOverviewHtml(ValidationResult validationResult, HealthCheckResult? bootstrapHealth)
    {
        if (bootstrapHealth == null)
        {
            return string.Empty;
        }

        var shellModifier = bootstrapHealth.Disposition switch
        {
            HealthCheckDisposition.Healthy => "healthy",
            HealthCheckDisposition.Protected => "protected",
            HealthCheckDisposition.TransientFailure => "transient",
            HealthCheckDisposition.Inconclusive => "inconclusive",
            HealthCheckDisposition.Unhealthy => "unhealthy",
            _ => "inconclusive"
        };

        var badgeClass = bootstrapHealth.Disposition switch
        {
            HealthCheckDisposition.Healthy => "status-pill status-pill--success",
            HealthCheckDisposition.Protected => "status-pill status-pill--info",
            HealthCheckDisposition.TransientFailure => "status-pill status-pill--warn",
            HealthCheckDisposition.Inconclusive => "status-pill status-pill--info",
            HealthCheckDisposition.Unhealthy => "status-pill status-pill--warn",
            _ => "status-pill status-pill--info"
        };

        var badgeLabel = bootstrapHealth.Disposition switch
        {
            HealthCheckDisposition.Healthy => "Healthy bootstrap",
            HealthCheckDisposition.Protected => "Protected endpoint",
            HealthCheckDisposition.TransientFailure => "Transient constraint",
            HealthCheckDisposition.Inconclusive => "Inconclusive bootstrap",
            HealthCheckDisposition.Unhealthy => "Unhealthy bootstrap",
            _ => "Bootstrap state unknown"
        };

        var handshakeStatus = bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode
            ? $"HTTP {statusCode}"
            : "n/a";
        var handshakeLatency = bootstrapHealth.ResponseTimeMs > 0
            ? $"{bootstrapHealth.ResponseTimeMs:F1} ms"
            : "n/a";
        var protocolVersion = validationResult.ProtocolVersion
            ?? bootstrapHealth.ProtocolVersion
            ?? validationResult.ServerConfig.ProtocolVersion
            ?? "n/a";
        var serverVersion = !string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion) &&
                            !string.Equals(bootstrapHealth.ServerVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? bootstrapHealth.ServerVersion
            : "n/a";
        var deferredLabel = bootstrapHealth.AllowsDeferredValidation && !bootstrapHealth.IsHealthy
            ? "Yes"
            : "No";
        var noteHtml = string.IsNullOrWhiteSpace(bootstrapHealth.ErrorMessage)
            ? string.Empty
            : $@"
        <div class=""bootstrap-note""><strong>Bootstrap Note:</strong> {System.Net.WebUtility.HtmlEncode(bootstrapHealth.ErrorMessage)}</div>";

        return $@"
            <div class=""section"">
                <h2>Connectivity & Session Bootstrap</h2>
                <div class=""bootstrap-shell bootstrap-shell--{shellModifier}"">
                    <div class=""bootstrap-header"">
                        <div>
                            <div class=""bootstrap-kicker"">Preflight Connectivity</div>
                            <div class=""bootstrap-title"">{System.Net.WebUtility.HtmlEncode(GetBootstrapHeadline(bootstrapHealth))}</div>
                            <div class=""bootstrap-copy"">{System.Net.WebUtility.HtmlEncode(GetBootstrapNarrative(bootstrapHealth))}</div>
                        </div>
                        <span class=""{badgeClass}"">{System.Net.WebUtility.HtmlEncode(badgeLabel)}</span>
                    </div>
                    <div class=""bootstrap-grid"">
                        {RenderBootstrapCard("Bootstrap State", GetBootstrapStateLabel(bootstrapHealth))}
                        {RenderBootstrapCard("Deferred Validation", deferredLabel)}
                        {RenderBootstrapCard("Handshake HTTP", handshakeStatus)}
                        {RenderBootstrapCard("Handshake Latency", handshakeLatency)}
                        {RenderBootstrapCard("Negotiated Protocol", protocolVersion)}
                        {RenderBootstrapCard("Observed Server Version", serverVersion)}
                    </div>
                    <div class=""bootstrap-meta"">
                        <span class=""meta-tag"">Server Profile: {System.Net.WebUtility.HtmlEncode(validationResult.ServerProfile.ToString())}</span>
                        <span class=""meta-tag"">Profile Source: {System.Net.WebUtility.HtmlEncode(validationResult.ServerProfileSource.ToString())}</span>
                    </div>{noteHtml}
                </div>
            </div>";

        static string RenderBootstrapCard(string label, string value)
        {
            return $@"<div class=""bootstrap-card""><div class=""bootstrap-card__label"">{System.Net.WebUtility.HtmlEncode(label)}</div><div class=""bootstrap-card__value"">{System.Net.WebUtility.HtmlEncode(value)}</div></div>";
        }
    }

    private static XElement? BuildBootstrapHealthElement(ValidationResult validationResult)
    {
        var bootstrapHealth = ResolveBootstrapHealth(validationResult);
        if (bootstrapHealth == null)
        {
            return null;
        }

        var element = new XElement("BootstrapHealth",
            new XAttribute("disposition", bootstrapHealth.Disposition.ToString()),
            new XAttribute("isHealthy", bootstrapHealth.IsHealthy),
            new XAttribute("allowsDeferredValidation", bootstrapHealth.AllowsDeferredValidation),
            new XElement("ResponseTimeMs", bootstrapHealth.ResponseTimeMs.ToString("F1", CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ErrorMessage))
        {
            element.Add(new XElement("ErrorMessage", bootstrapHealth.ErrorMessage));
        }

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion))
        {
            element.Add(new XElement("ServerVersion", bootstrapHealth.ServerVersion));
        }

        var protocolVersion = validationResult.ProtocolVersion ?? bootstrapHealth.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            element.Add(new XElement("ProtocolVersion", protocolVersion));
        }

        if (bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode)
        {
            element.Add(new XElement("HandshakeHttpStatus", statusCode));
        }

        return element;
    }

    private static XElement? BuildClientCompatibilityElement(ValidationResult validationResult)
    {
        if (validationResult.ClientCompatibility?.Assessments.Count > 0 != true)
        {
            return null;
        }

        var element = new XElement("ClientCompatibility");

        if (validationResult.ClientCompatibility.RequestedProfiles.Count > 0)
        {
            element.Add(new XElement("RequestedProfiles",
                validationResult.ClientCompatibility.RequestedProfiles.Select(profileId => new XElement("ProfileId", profileId))));
        }

        element.Add(validationResult.ClientCompatibility.Assessments.Select(assessment =>
        {
            var profileElement = new XElement("Profile",
                new XAttribute("id", assessment.ProfileId),
                new XAttribute("status", assessment.Status.ToString()),
                new XAttribute("passedRequirements", assessment.PassedRequirements),
                new XAttribute("warningRequirements", assessment.WarningRequirements),
                new XAttribute("failedRequirements", assessment.FailedRequirements),
                new XElement("DisplayName", assessment.DisplayName),
                new XElement("Revision", assessment.Revision),
                new XElement("EvidenceBasis", assessment.EvidenceBasis.ToString()),
                new XElement("Summary", assessment.Summary));

            if (!string.IsNullOrWhiteSpace(assessment.DocumentationUrl))
            {
                profileElement.Add(new XElement("DocumentationUrl", assessment.DocumentationUrl));
            }

            if (assessment.Requirements.Count > 0)
            {
                profileElement.Add(new XElement("Requirements",
                    assessment.Requirements.Select(requirement =>
                    {
                        var requirementElement = new XElement("Requirement",
                            new XAttribute("id", requirement.RequirementId),
                            new XAttribute("level", requirement.Level.ToString()),
                            new XAttribute("outcome", requirement.Outcome.ToString()),
                            new XElement("Title", requirement.Title),
                            new XElement("Summary", requirement.Summary),
                            new XElement("EvidenceBasis", requirement.EvidenceBasis.ToString()));

                        if (requirement.RuleIds.Count > 0)
                        {
                            requirementElement.Add(new XElement("RuleIds", requirement.RuleIds.Select(ruleId => new XElement("RuleId", ruleId))));
                        }

                        if (requirement.ExampleComponents.Count > 0)
                        {
                            requirementElement.Add(new XElement("ExampleComponents", requirement.ExampleComponents.Select(component => new XElement("Component", component))));
                        }

                        if (!string.IsNullOrWhiteSpace(requirement.Recommendation))
                        {
                            requirementElement.Add(new XElement("Recommendation", requirement.Recommendation));
                        }

                        if (!string.IsNullOrWhiteSpace(requirement.DocumentationUrl))
                        {
                            requirementElement.Add(new XElement("DocumentationUrl", requirement.DocumentationUrl));
                        }

                        return requirementElement;
                    })));
            }

            return profileElement;
        }));

        return element;
    }

    private static HealthCheckResult? ResolveBootstrapHealth(ValidationResult validationResult)
    {
        if (validationResult.BootstrapHealth != null)
        {
            return validationResult.BootstrapHealth;
        }

        if (validationResult.InitializationHandshake == null)
        {
            return null;
        }

        return new HealthCheckResult
        {
            IsHealthy = validationResult.InitializationHandshake.IsSuccessful,
            Disposition = ValidationReliability.ClassifyHealthCheck(validationResult.InitializationHandshake),
            ResponseTimeMs = validationResult.InitializationHandshake.Transport.Duration.TotalMilliseconds,
            ServerVersion = validationResult.InitializationHandshake.Payload?.ServerInfo?.Version,
            ProtocolVersion = validationResult.InitializationHandshake.Payload?.ProtocolVersion,
            ErrorMessage = validationResult.InitializationHandshake.IsSuccessful ? null : validationResult.InitializationHandshake.Error,
            InitializationDetails = validationResult.InitializationHandshake
        };
    }

    private static string GetBootstrapHeadline(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Validation started from a clean initialize handshake.",
        HealthCheckDisposition.Protected => "Validation crossed an expected authentication boundary during bootstrap.",
        HealthCheckDisposition.TransientFailure => "Validation continued after a retry-worthy bootstrap constraint.",
        HealthCheckDisposition.Inconclusive => "Validation continued despite an inconclusive bootstrap handshake.",
        HealthCheckDisposition.Unhealthy => "Validation observed a definitive bootstrap failure.",
        _ => "Validation bootstrap state is unknown."
    };

    private static string GetBootstrapNarrative(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Connectivity, initialize negotiation, and readiness checks completed cleanly before category execution began.",
        HealthCheckDisposition.Protected => "The endpoint was reachable but protected, so validation continued because the outcome indicated security boundaries rather than an unreachable server.",
        HealthCheckDisposition.TransientFailure => "The preflight handshake matched a transient capacity or transport signal, so validation continued to gather evidence without treating the endpoint as hard down.",
        HealthCheckDisposition.Inconclusive => "The server responded, but the initialize handshake did not fully establish readiness. Later categories provide the authoritative evidence.",
        HealthCheckDisposition.Unhealthy => "Bootstrap failed definitively and subsequent evidence, if any, should be interpreted as partial.",
        _ => "Bootstrap classification was not available."
    };

    private static string GetBootstrapStateLabel(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Healthy",
        HealthCheckDisposition.Protected => "Reachable (Protected)",
        HealthCheckDisposition.TransientFailure => "Transient Failure",
        HealthCheckDisposition.Inconclusive => "Inconclusive",
        HealthCheckDisposition.Unhealthy => "Unhealthy",
        _ => "Unknown"
    };

    private string GenerateSecurityDetailsHtml(ValidationResult validationResult, bool verbose)
    {
        if (validationResult.SecurityTesting == null || !verbose)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var security = validationResult.SecurityTesting;

        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Security Assessment Details</h2>");
        sb.AppendLine($"  <p><strong>Security Score:</strong> {security.SecurityScore:F1}% &middot; <strong>Vulnerabilities:</strong> {security.Vulnerabilities.Count}</p>");

        if (security.AuthenticationTestResult?.TestScenarios?.Any() == true)
        {
            sb.AppendLine("  <div class=\"detailed-section\">");
            sb.AppendLine("    <h3>Authentication Scenarios</h3>");
            sb.AppendLine("    <table class=\"data-table\">");
            sb.AppendLine("      <thead><tr><th align=\"left\">Scenario</th><th align=\"left\">Method</th><th align=\"left\">Expected</th><th align=\"left\">Actual</th><th align=\"left\">Analysis</th></tr></thead>");
            sb.AppendLine("      <tbody>");
            foreach (var scenario in security.AuthenticationTestResult.TestScenarios)
            {
                sb.AppendLine($"        <tr><td>{System.Net.WebUtility.HtmlEncode(scenario.ScenarioName)}</td><td><code>{System.Net.WebUtility.HtmlEncode(scenario.Method)}</code></td><td>{System.Net.WebUtility.HtmlEncode(scenario.ExpectedBehavior)}</td><td>{System.Net.WebUtility.HtmlEncode(scenario.ActualBehavior)}</td><td>{System.Net.WebUtility.HtmlEncode(scenario.Analysis)}</td></tr>");
            }
            sb.AppendLine("      </tbody>");
            sb.AppendLine("    </table>");
            sb.AppendLine("  </div>");
        }

        if (security.AttackSimulations?.Any() == true)
        {
            sb.AppendLine("  <div class=\"detailed-section\">");
            sb.AppendLine("    <h3>Attack Simulations</h3>");
            sb.AppendLine("    <table class=\"data-table\">");
            sb.AppendLine("      <thead><tr><th align=\"left\">Vector</th><th align=\"left\">Description</th><th align=\"left\">Result</th><th align=\"left\">Server Response</th></tr></thead>");
            sb.AppendLine("      <tbody>");
            foreach (var attack in security.AttackSimulations)
            {
                var resultLabel = attack.DefenseSuccessful ? "BLOCKED" : "REFLECTED";
                var response = string.IsNullOrEmpty(attack.ServerResponse) ? "-" : attack.ServerResponse.Replace("\n", " ");
                sb.AppendLine($"        <tr><td>{System.Net.WebUtility.HtmlEncode(attack.AttackVector)}</td><td>{System.Net.WebUtility.HtmlEncode(attack.Description)}</td><td>{resultLabel}</td><td><code>{System.Net.WebUtility.HtmlEncode(response)}</code></td></tr>");
            }
            sb.AppendLine("      </tbody>");
            sb.AppendLine("    </table>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private string GenerateToolDetailsHtml(ValidationResult validationResult, bool verbose)
    {
        if (validationResult.ToolValidation == null || !verbose)
        {
            return string.Empty;
        }

        var tools = validationResult.ToolValidation;
        if (tools.ToolResults == null || !tools.ToolResults.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Tool Validation Details</h2>");
        sb.AppendLine($"  <p><strong>Tools Discovered:</strong> {tools.ToolsDiscovered} &middot; <strong>Passed:</strong> {tools.ToolsTestPassed} &middot; <strong>Failed:</strong> {tools.ToolsTestFailed}</p>");

        if (tools.AuthenticationSecurity != null)
        {
            var auth = tools.AuthenticationSecurity;
            var enforcementPill = BuildPill(tools.AuthenticationProperlyEnforced, "Enforced", "Open Gate");
            var requirementPill = BuildPill(auth.AuthenticationRequired, "Required", "Optional", "pill--info", "pill--warn");
            var challengeStatus = auth.ChallengeStatusCode.HasValue ? $"HTTP {auth.ChallengeStatusCode}" : "n/a";
            var challengeDuration = auth.ChallengeDurationMs > 0 ? $"{auth.ChallengeDurationMs:F1} ms" : "-";
            var securityScore = auth.SecurityScore > 0 ? $"{auth.SecurityScore:F1}%" : "n/a";

            sb.AppendLine("  <div class=\"detailed-section auth-summary\">");
            sb.AppendLine("    <h3>Authentication Summary</h3>");
            sb.AppendLine("    <div class=\"auth-grid\">");
            sb.AppendLine(RenderAuthStat("Enforcement", enforcementPill));
            sb.AppendLine(RenderAuthStat("Requirement", requirementPill));
            sb.AppendLine(RenderAuthStat("Challenge Status", System.Net.WebUtility.HtmlEncode(challengeStatus)));
            sb.AppendLine(RenderAuthStat("Challenge Latency", System.Net.WebUtility.HtmlEncode(challengeDuration)));
            sb.AppendLine(RenderAuthStat("Security Score", System.Net.WebUtility.HtmlEncode(securityScore)));
            sb.AppendLine("    </div>");

            if (!string.IsNullOrWhiteSpace(auth.AuthMetadata?.Resource))
            {
                sb.AppendLine(RenderAuthRow("Protected Resource", $"<code>{System.Net.WebUtility.HtmlEncode(auth.AuthMetadata.Resource)}</code>"));
            }

            if (!string.IsNullOrWhiteSpace(auth.WwwAuthenticateHeader))
            {
                sb.AppendLine(RenderAuthRow("WWW-Authenticate", $"<code>{System.Net.WebUtility.HtmlEncode(auth.WwwAuthenticateHeader)}</code>"));
            }

            if (auth.AuthMetadata?.AuthorizationServers?.Any() == true)
            {
                sb.AppendLine(RenderMetaTags("Authorization Servers", auth.AuthMetadata.AuthorizationServers));
            }

            if (auth.AuthMetadata?.ScopesSupported?.Any() == true)
            {
                sb.AppendLine(RenderMetaTags("Scopes", auth.AuthMetadata.ScopesSupported));
            }

            if (auth.AuthMetadata?.BearerMethodsSupported?.Any() == true)
            {
                sb.AppendLine(RenderMetaTags("Bearer Methods", auth.AuthMetadata.BearerMethodsSupported));
            }

            if (auth.Findings.Any())
            {
                sb.AppendLine("    <ul class=\"finding-list\">");
                foreach (var finding in auth.Findings)
                {
                    sb.AppendLine($"      <li>{System.Net.WebUtility.HtmlEncode(finding)}</li>");
                }
                sb.AppendLine("    </ul>");
            }

            sb.AppendLine("  </div>");

            string RenderAuthStat(string label, string valueHtml)
            {
                var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
                return $"      <div class=\"auth-stat\"><div class=\"stat-label\">{encodedLabel}</div><div class=\"stat-value\">{valueHtml}</div></div>";
            }

            string RenderAuthRow(string label, string valueHtml)
            {
                var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
                return $"    <div class=\"auth-row\"><div class=\"stat-label\">{encodedLabel}</div><div>{valueHtml}</div></div>";
            }

            string RenderMetaTags(string label, IEnumerable<string> values)
            {
                var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
                var tags = string.Join(string.Empty, values.Select(v => $"<span class=\"meta-tag\">{System.Net.WebUtility.HtmlEncode(v)}</span>"));
                return $"    <div class=\"auth-row\"><div class=\"stat-label\">{encodedLabel}</div><div class=\"meta-tags\">{tags}</div></div>";
            }

            string BuildPill(bool outcome, string positiveLabel, string negativeLabel, string positiveClass = "pill--success", string negativeClass = "pill--warn")
            {
                var cssClass = outcome ? positiveClass : negativeClass;
                var label = outcome ? positiveLabel : negativeLabel;
                return $"<span class=\"pill {cssClass}\">{System.Net.WebUtility.HtmlEncode(label)}</span>";
            }
        }

        sb.AppendLine("  <div class=\"detailed-section\">");
        sb.AppendLine("    <table class=\"data-table\">");
        sb.AppendLine("      <thead><tr><th align=\"left\">Tool</th><th align=\"left\">Status</th><th align=\"left\">Execution Time (ms)</th></tr></thead>");
        sb.AppendLine("      <tbody>");
        foreach (var tool in tools.ToolResults)
        {
            sb.AppendLine($"        <tr><td><code>{System.Net.WebUtility.HtmlEncode(tool.ToolName)}</code></td><td>{tool.Status}</td><td>{tool.ExecutionTimeMs:F2}</td></tr>");
        }
        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private string GenerateResourceDetailsHtml(ValidationResult validationResult, bool verbose)
    {
        if (validationResult.ResourceTesting == null || !verbose)
        {
            return string.Empty;
        }

        var resources = validationResult.ResourceTesting;
        if (resources.ResourceResults == null || !resources.ResourceResults.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Resource Capabilities</h2>");
        sb.AppendLine($"  <p><strong>Resources Discovered:</strong> {resources.ResourcesDiscovered} &middot; <strong>Accessible:</strong> {resources.ResourcesAccessible}</p>");

        sb.AppendLine("  <div class=\"detailed-section\">");
        sb.AppendLine("    <table class=\"data-table\">");
        sb.AppendLine("      <thead><tr><th align=\"left\">Name</th><th align=\"left\">URI</th><th align=\"left\">MIME Type</th><th align=\"left\">Size</th><th align=\"left\">Status</th></tr></thead>");
        sb.AppendLine("      <tbody>");
        foreach (var res in resources.ResourceResults)
        {
            var size = res.ContentSize?.ToString() ?? "-";
            sb.AppendLine($"        <tr><td>{System.Net.WebUtility.HtmlEncode(res.ResourceName)}</td><td><code>{System.Net.WebUtility.HtmlEncode(res.ResourceUri)}</code></td><td>{System.Net.WebUtility.HtmlEncode(res.MimeType ?? "-")}</td><td>{size}</td><td>{res.Status}</td></tr>");
        }
        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private string GeneratePerformanceDetailsHtml(ValidationResult validationResult, bool verbose)
    {
        if (validationResult.PerformanceTesting == null || !verbose)
        {
            return string.Empty;
        }

        var perf = validationResult.PerformanceTesting;
        if (perf.LoadTesting == null)
        {
            return string.Empty;
        }

        if (!PerformanceMeasurementEvaluator.HasObservedMetrics(perf))
        {
            var unavailableReason = PerformanceMeasurementEvaluator.GetUnavailableReason(
                perf,
                "Performance measurements were not captured before the run ended.");
            var sbUnavailable = new StringBuilder();
            sbUnavailable.AppendLine("<div class=\"section\">");
            sbUnavailable.AppendLine("  <h2>Performance Metrics</h2>");
            sbUnavailable.AppendLine("  <div class=\"detailed-section\">");
            sbUnavailable.AppendLine($"    <p><strong>Status:</strong> {System.Net.WebUtility.HtmlEncode(perf.Status.ToString())}</p>");
            sbUnavailable.AppendLine("    <p><strong>Measurements:</strong> Unavailable</p>");
            sbUnavailable.AppendLine($"    <p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(unavailableReason)}</p>");

            if (perf.CriticalErrors.Any())
            {
                sbUnavailable.AppendLine("    <div class=\"error-list\">");
                foreach (var error in perf.CriticalErrors)
                {
                    sbUnavailable.AppendLine($"      <div class=\"error-item\">• {System.Net.WebUtility.HtmlEncode(error)}</div>");
                }
                sbUnavailable.AppendLine("    </div>");
            }

            sbUnavailable.AppendLine("  </div>");
            sbUnavailable.AppendLine("</div>");
            return sbUnavailable.ToString();
        }

        var avgLatency = perf.LoadTesting.AverageResponseTimeMs;
        var p95Latency = perf.LoadTesting.P95ResponseTimeMs;
        var errorRate = perf.LoadTesting.ErrorRate;

        static string GetLatencyClass(double value)
        {
            // Targets: 
            // - 22300 ms: excellent client-perceived latency
            // - 30022800 ms: acceptable under normal load
            // - >800 ms: degraded, worth investigating
            if (value <= 300) return "metric-card metric-card--good";
            if (value <= 800) return "metric-card metric-card--warn";
            return "metric-card metric-card--poor";
        }

        static string GetErrorRateClass(double value)
        {
            // Targets:
            // - 22 1%: healthy
            // - 1%225%: noisy but usually tolerable
            // - >5%: problematic
            if (value <= 1) return "metric-card metric-card--good";
            if (value <= 5) return "metric-card metric-card--warn";
            return "metric-card metric-card--poor";
        }

        var avgLatencyClass = GetLatencyClass(avgLatency);
        var p95LatencyClass = GetLatencyClass(p95Latency);
        var errorRateClass = GetErrorRateClass(errorRate);

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Performance Metrics</h2>");
        sb.AppendLine("  <div class=\"detailed-section\">");

        if (perf.Status != TestStatus.Passed && !string.IsNullOrWhiteSpace(perf.Message))
        {
            sb.AppendLine($"    <p class=\"capability-footnote subtle\"><strong>Status:</strong> {System.Net.WebUtility.HtmlEncode(perf.Status.ToString())}. {System.Net.WebUtility.HtmlEncode(perf.Message)}</p>");
        }

        sb.AppendLine("    <div class=\"metric-grid\">");
        sb.AppendLine($"      <div class=\"{avgLatencyClass}\"><div class=\"metric-value\">{perf.LoadTesting.AverageResponseTimeMs:F2} ms</div><div class=\"metric-label\">Average Latency</div></div>");
        sb.AppendLine($"      <div class=\"{p95LatencyClass}\"><div class=\"metric-value\">{perf.LoadTesting.P95ResponseTimeMs:F2} ms</div><div class=\"metric-label\">P95 Latency</div></div>");
        sb.AppendLine($"      <div class=\"metric-card\"><div class=\"metric-value\">{perf.LoadTesting.RequestsPerSecond:F2} req/sec</div><div class=\"metric-label\">Throughput</div></div>");
        sb.AppendLine($"      <div class=\"{errorRateClass}\"><div class=\"metric-value\">{perf.LoadTesting.ErrorRate:F2}%</div><div class=\"metric-label\">Error Rate</div></div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <p class=\"capability-footnote subtle\">Latency in milliseconds; throughput in requests per second; error rate as percentage of failed requests.</p>");
        sb.AppendLine("    <p class=\"capability-footnote subtle\"><strong>Targets:</strong> Avg latency  5 300ms (ideal),  5 800ms (acceptable); P95 latency  5 1500ms; error rate  5 1%.</p>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    private string GenerateRuleInsightsHtml(ValidationResult validationResult, ReportingConfig? reportConfig)
    {
        var violations = validationResult.ProtocolCompliance?.Violations ?? new List<ComplianceViolation>();
        var vulnerabilities = validationResult.SecurityTesting?.Vulnerabilities ?? new List<SecurityVulnerability>();
        if (!violations.Any() && !vulnerabilities.Any())
        {
            return string.Empty;
        }

        reportConfig ??= new ReportingConfig();
        var includeSpec = reportConfig.IncludeSpecReferences;
        var severityCounts = BuildSeverityBuckets(violations, vulnerabilities);
        const int maxRows = 40;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Rule & Severity Insights</h2>");
        sb.AppendLine("  <p>Stable identifiers and severity bands make regressions easier to triage.</p>");
        sb.AppendLine("  <div class=\"severity-grid\">");
        foreach (var severity in SeverityOrder)
        {
            var count = severityCounts.TryGetValue(severity, out var value) ? value : 0;
            sb.AppendLine(RenderSeverityCard(severity, count));
        }
        sb.AppendLine("  </div>");

        if (violations.Any())
        {
            var trimmed = violations.Take(maxRows).ToList();
            var specHeader = includeSpec ? "<th align=\"left\">Spec</th>" : string.Empty;
            sb.AppendLine("  <div class=\"detailed-section\">");
            sb.AppendLine("    <h3>Protocol & Policy Violations</h3>");
            sb.AppendLine($"    <table class=\"data-table rule-table\"><thead><tr><th align=\"left\">Rule</th><th align=\"left\">Source</th><th align=\"left\">Severity</th><th align=\"left\">Category</th><th align=\"left\">Details</th>{specHeader}</tr></thead><tbody>");
            foreach (var violation in trimmed)
            {
                var ruleCell = BuildRuleIdentifierCell(violation.CheckId, violation.Rule);
                var sourceCell = BuildSourceBadge(ValidationRuleSourceClassifier.GetLabel(violation));
                var severityBadge = BuildSeverityBadge(MapViolationSeverity(violation.Severity));
                var categoryCell = string.IsNullOrWhiteSpace(violation.Category)
                    ? "<span class=\"spec-placeholder\">—</span>"
                    : System.Net.WebUtility.HtmlEncode(violation.Category);
                var detailsCell = BuildDetailsCell(violation.Description, violation.Recommendation);
                var specCell = includeSpec ? $"<td>{BuildSpecReferenceCell(violation.SpecReference)}</td>" : string.Empty;
                sb.AppendLine($"      <tr><td>{ruleCell}</td><td>{sourceCell}</td><td>{severityBadge}</td><td>{categoryCell}</td><td>{detailsCell}</td>{specCell}</tr>");
            }
            sb.AppendLine("    </tbody></table>");
            if (violations.Count > trimmed.Count)
            {
                sb.AppendLine($"    <p class=\"capability-footnote subtle\">Showing first {trimmed.Count} of {violations.Count} findings.</p>");
            }
            sb.AppendLine("  </div>");
        }

        if (vulnerabilities.Any())
        {
            var trimmedVulns = vulnerabilities.Take(maxRows).ToList();
            sb.AppendLine("  <div class=\"detailed-section\">");
            sb.AppendLine("    <h3>Security Vulnerabilities</h3>");
            sb.AppendLine("    <table class=\"data-table rule-table\"><thead><tr><th align=\"left\">Issue</th><th align=\"left\">Source</th><th align=\"left\">Severity</th><th align=\"left\">Component</th><th align=\"left\">Details</th></tr></thead><tbody>");
            foreach (var vulnerability in trimmedVulns)
            {
                var issueCell = BuildVulnerabilityIssueCell(vulnerability);
                var sourceCell = BuildSourceBadge(ValidationRuleSourceClassifier.GetLabel(vulnerability));
                var severityBadge = BuildSeverityBadge(MapVulnerabilitySeverity(vulnerability.Severity));
                var componentCell = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent)
                    ? "<span class=\"spec-placeholder\">—</span>"
                    : System.Net.WebUtility.HtmlEncode(vulnerability.AffectedComponent);
                var detailsCell = BuildDetailsCell(vulnerability.Description, vulnerability.Remediation);
                sb.AppendLine($"      <tr><td>{issueCell}</td><td>{sourceCell}</td><td>{severityBadge}</td><td>{componentCell}</td><td>{detailsCell}</td></tr>");
            }
            sb.AppendLine("    </tbody></table>");
            if (vulnerabilities.Count > trimmedVulns.Count)
            {
                sb.AppendLine($"    <p class=\"capability-footnote subtle\">Showing first {trimmedVulns.Count} of {vulnerabilities.Count} vulnerabilities.</p>");
            }
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string RenderSeverityCard(string severity, int count)
    {
        var cssToken = SeverityCssToken(severity);
        var safeSeverity = System.Net.WebUtility.HtmlEncode(severity);
        var footnote = System.Net.WebUtility.HtmlEncode(GetSeverityFootnote(severity));
        return $"    <div class=\"severity-card severity-card--{cssToken}\"><div class=\"severity-label\">{safeSeverity}</div><div class=\"severity-value\">{count}</div><div class=\"severity-footnote\">{footnote}</div></div>";
    }

    private static string BuildSeverityBadge(string? severityLabel)
    {
        if (string.IsNullOrWhiteSpace(severityLabel))
        {
            return "<span class=\"spec-placeholder\">—</span>";
        }

        var trimmed = severityLabel.Trim();
        var cssToken = SeverityCssToken(trimmed);
        var safeLabel = System.Net.WebUtility.HtmlEncode(trimmed);
        return $"<span class=\"severity-badge severity-badge--{cssToken}\">{safeLabel}</span>";
    }

    private static string BuildSourceBadge(string sourceLabel)
    {
        var safeLabel = System.Net.WebUtility.HtmlEncode(sourceLabel);
        return $"<span class=\"pill pill--info\">{safeLabel}</span>";
    }

    private static string BuildRuleIdentifierCell(string? checkId, string? rule)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(checkId))
        {
            builder.Append($"<code>{System.Net.WebUtility.HtmlEncode(checkId)}</code>");
        }
        else
        {
            builder.Append("<span class=\"spec-placeholder\">—</span>");
        }

        if (!string.IsNullOrWhiteSpace(rule))
        {
            builder.Append($"<span class=\"rule-secondary\">{System.Net.WebUtility.HtmlEncode(rule)}</span>");
        }

        return builder.ToString();
    }

    private static string BuildVulnerabilityIssueCell(SecurityVulnerability vulnerability)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(vulnerability.Id))
        {
            builder.Append($"<code>{System.Net.WebUtility.HtmlEncode(vulnerability.Id)}</code>");
        }
        else if (!string.IsNullOrWhiteSpace(vulnerability.Name))
        {
            builder.Append($"<code>{System.Net.WebUtility.HtmlEncode(vulnerability.Name)}</code>");
        }
        else
        {
            builder.Append("<span class=\"spec-placeholder\">—</span>");
        }

        if (!string.IsNullOrWhiteSpace(vulnerability.Name))
        {
            builder.Append($"<span class=\"rule-secondary\">{System.Net.WebUtility.HtmlEncode(vulnerability.Name)}</span>");
        }

        return builder.ToString();
    }

    private static string BuildDetailsCell(string? description, string? recommendation)
    {
        var safeDescription = string.IsNullOrWhiteSpace(description)
            ? "<span class=\"spec-placeholder\">No description</span>"
            : System.Net.WebUtility.HtmlEncode(description);

        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return safeDescription;
        }

        var safeRecommendation = System.Net.WebUtility.HtmlEncode(recommendation);
        return $"{safeDescription}<div class=\"rule-recommendation\">{safeRecommendation}</div>";
    }

    private static string BuildSpecReferenceCell(string? specReference)
    {
        if (string.IsNullOrWhiteSpace(specReference))
        {
            return "<span class=\"spec-placeholder\">—</span>";
        }

        var trimmed = specReference.Trim();
        var encoded = System.Net.WebUtility.HtmlEncode(trimmed);
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            var href = System.Net.WebUtility.HtmlEncode(uri.ToString());
            return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener noreferrer\">{encoded}</a>";
        }

        return encoded;
    }

    private static string GetSeverityFootnote(string severity)
    {
        return severity switch
        {
            "Critical" => "Immediate remediation",
            "High" => "Blocks production readiness",
            "Medium" => "Fix in next sprint",
            "Low" => "Advisory",
            "Informational" => "Context only",
            _ => "Tracked finding"
        };
    }

    private static Dictionary<string, int> BuildSeverityBuckets(IEnumerable<ComplianceViolation> violations, IEnumerable<SecurityVulnerability> vulnerabilities)
    {
        var buckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var severity in SeverityOrder)
        {
            buckets[severity] = 0;
        }

        foreach (var violation in violations)
        {
            var bucket = MapViolationSeverity(violation.Severity);
            buckets[bucket]++;
        }

        foreach (var vulnerability in vulnerabilities)
        {
            var bucket = MapVulnerabilitySeverity(vulnerability.Severity);
            buckets[bucket]++;
        }

        return buckets;
    }

    private static string MapViolationSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => "Critical",
            ViolationSeverity.High => "High",
            ViolationSeverity.Medium => "Medium",
            ViolationSeverity.Low => "Low",
            _ => "Low"
        };
    }

    private static string MapVulnerabilitySeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "Critical",
            VulnerabilitySeverity.High => "High",
            VulnerabilitySeverity.Medium => "Medium",
            VulnerabilitySeverity.Low => "Low",
            VulnerabilitySeverity.Informational => "Informational",
            _ => "Low"
        };
    }

    private static string SeverityCssToken(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            "informational" => "informational",
            _ => "low"
        };
    }

    private static readonly string[] SeverityOrder = new[] { "Critical", "High", "Medium", "Low", "Informational" };

    private XElement? BuildSeverityBreakdownElement(ValidationResult validationResult)
    {
        var violations = validationResult.ProtocolCompliance?.Violations ?? Enumerable.Empty<ComplianceViolation>();
        var vulnerabilities = validationResult.SecurityTesting?.Vulnerabilities ?? Enumerable.Empty<SecurityVulnerability>();

        if (!violations.Any() && !vulnerabilities.Any())
        {
            return null;
        }

        var buckets = BuildSeverityBuckets(violations, vulnerabilities);
        if (buckets.Values.All(value => value == 0))
        {
            return null;
        }

        var element = new XElement("SeverityBreakdown");
        foreach (var severity in SeverityOrder)
        {
            buckets.TryGetValue(severity, out var count);
            element.Add(new XElement("Severity",
                new XAttribute("level", severity),
                new XAttribute("count", count)));
        }

        return element;
    }

    private XElement BuildCapabilitySnapshotElement(CapabilitySummary snapshot)
    {
        var element = new XElement("CapabilitySnapshot");
        element.Add(CreateCapabilityProbeElement("tools/list", snapshot.DiscoveredToolsCount, snapshot.ToolListResponse?.StatusCode, snapshot.ToolListDurationMs, snapshot.ToolListingSucceeded, snapshot.ToolInvocationSucceeded));
        element.Add(CreateCapabilityProbeElement("resources/list", snapshot.DiscoveredResourcesCount, snapshot.ResourceListResponse?.StatusCode, snapshot.ResourceListDurationMs, snapshot.ResourceListingSucceeded));
        element.Add(CreateCapabilityProbeElement("prompts/list", snapshot.DiscoveredPromptsCount, snapshot.PromptListResponse?.StatusCode, snapshot.PromptListDurationMs, snapshot.PromptListingSucceeded));

        if (!string.IsNullOrWhiteSpace(snapshot.FirstToolName))
        {
            element.Add(new XElement("FirstTool", snapshot.FirstToolName));
        }

        return element;
    }

    private XElement CreateCapabilityProbeElement(string name, int discovered, int? statusCode, double durationMs, bool succeeded, bool? invocationSucceeded = null)
    {
        var probeElement = new XElement("Probe",
            new XAttribute("name", name),
            new XAttribute("discovered", discovered),
            new XAttribute("durationMs", durationMs.ToString("F1")),
            new XAttribute("succeeded", succeeded));

        if (statusCode.HasValue)
        {
            probeElement.Add(new XAttribute("httpStatus", statusCode.Value));
        }

        if (invocationSucceeded.HasValue)
        {
            probeElement.Add(new XAttribute("invocationSucceeded", invocationSucceeded.Value));
        }

        return probeElement;
    }

    private static string FormatDuration(double durationMs)
    {
        return durationMs > 0 ? $"{durationMs:F1} ms" : "-";
    }

    private static string FormatHttpStatus(int? statusCode)
    {
        return statusCode.HasValue ? $"HTTP {statusCode}" : "n/a";
    }

    private static string FormatProbeResultHtml(bool succeeded, string successText, string failureText)
    {
        var icon = succeeded ? "✅" : "⚠️";
        var text = succeeded ? successText : failureText;
        return $"{icon} {text}";
    }

    private static IReadOnlyList<SarifEntry> BuildSarifEntries(ValidationResult validationResult)
    {
        var entries = new List<SarifEntry>();

        AddFindingRange(entries, validationResult.ProtocolCompliance?.Findings, "protocol");
        AddFindingRange(entries, validationResult.ToolValidation?.Findings, "tools");
        AddFindingRange(entries, validationResult.ToolValidation?.AiReadinessFindings, "tools/ai-readiness");
        AddFindingRange(entries, validationResult.ResourceTesting?.Findings, "resources");
        AddFindingRange(entries, validationResult.PromptTesting?.Findings, "prompts");
        AddFindingRange(entries, validationResult.SecurityTesting?.Findings, "security");
        AddFindingRange(entries, validationResult.PerformanceTesting?.Findings, "performance");
        AddFindingRange(entries, validationResult.ErrorHandling?.Findings, "error-handling");

        if (validationResult.ToolValidation?.AuthenticationSecurity?.StructuredFindings is { Count: > 0 } authFindings)
        {
            AddFindingRange(entries, authFindings, "auth");
        }

        if (validationResult.ProtocolCompliance?.Violations is { Count: > 0 } violations)
        {
            entries.AddRange(violations.Select(violation => CreateSarifEntry(violation)));
        }

        if (validationResult.ToolValidation?.ToolResults is { Count: > 0 } toolResults)
        {
            foreach (var tool in toolResults)
            {
                AddFindingRange(entries, tool.Findings, tool.ToolName);
            }
        }

        if (validationResult.ResourceTesting?.ResourceResults is { Count: > 0 } resourceResults)
        {
            foreach (var resource in resourceResults)
            {
                AddFindingRange(entries, resource.Findings, string.IsNullOrWhiteSpace(resource.ResourceUri) ? resource.ResourceName : resource.ResourceUri);
            }
        }

        if (validationResult.PromptTesting?.PromptResults is { Count: > 0 } promptResults)
        {
            foreach (var prompt in promptResults)
            {
                AddFindingRange(entries, prompt.Findings, prompt.PromptName);
            }
        }

        if (validationResult.SecurityTesting?.Vulnerabilities is { Count: > 0 } vulnerabilities)
        {
            entries.AddRange(vulnerabilities.Select(CreateSarifEntry));
        }

        return entries;
    }

    private static void AddFindingRange(List<SarifEntry> entries, IEnumerable<ValidationFinding>? findings, string defaultComponent)
    {
        if (findings == null)
        {
            return;
        }

        foreach (var finding in findings)
        {
            entries.Add(CreateSarifEntry(finding, defaultComponent));
        }
    }

    private static SarifEntry CreateSarifEntry(ValidationFinding finding, string defaultComponent)
    {
        var component = string.IsNullOrWhiteSpace(finding.Component) ? defaultComponent : finding.Component;
        var properties = new Dictionary<string, object?>
        {
            ["source"] = "structured-finding",
            ["authority"] = finding.EffectiveSourceLabel,
            ["category"] = finding.Category,
            ["component"] = component,
            ["severity"] = finding.Severity.ToString()
        };

        if (!string.IsNullOrWhiteSpace(finding.Recommendation))
        {
            properties["recommendation"] = finding.Recommendation;
        }

        if (finding.Metadata.Count > 0)
        {
            properties["metadata"] = finding.Metadata;
        }

        return new SarifEntry(
            RuleId: finding.RuleId,
            Category: string.IsNullOrWhiteSpace(finding.Category) ? "validation" : finding.Category,
            Component: component,
            Level: MapFindingLevel(finding.Severity),
            Message: finding.Summary,
            ShortDescription: finding.Summary,
            FullDescription: finding.Summary,
            Recommendation: finding.Recommendation,
            HelpUri: TryGetHelpUri(finding.Metadata),
            Properties: properties,
                Source: finding.EffectiveSourceLabel,
            Fingerprint: $"finding|{finding.RuleId}|{component}|{finding.Summary}");
    }

    private static SarifEntry CreateSarifEntry(ComplianceViolation violation)
    {
        var ruleId = string.IsNullOrWhiteSpace(violation.CheckId)
            ? BuildFallbackRuleId("MCP.PROTOCOL", violation.Category, violation.Rule, violation.Description)
            : violation.CheckId;

        var component = string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category;
        var properties = new Dictionary<string, object?>
        {
            ["source"] = "protocol-violation",
            ["authority"] = ValidationRuleSourceClassifier.GetLabel(violation),
            ["category"] = violation.Category,
            ["component"] = component,
            ["severity"] = violation.Severity.ToString(),
            ["rule"] = violation.Rule
        };

        if (!string.IsNullOrWhiteSpace(violation.Recommendation))
        {
            properties["recommendation"] = violation.Recommendation;
        }

        if (violation.Context.Count > 0)
        {
            properties["context"] = violation.Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
        }

        return new SarifEntry(
            RuleId: ruleId,
            Category: string.IsNullOrWhiteSpace(violation.Category) ? "ProtocolCompliance" : violation.Category,
            Component: component,
            Level: MapViolationLevel(violation.Severity),
            Message: violation.Description,
            ShortDescription: violation.Rule ?? violation.Description,
            FullDescription: violation.Description,
            Recommendation: violation.Recommendation,
            HelpUri: violation.SpecReference,
            Properties: properties,
                Source: ValidationRuleSourceClassifier.GetLabel(violation),
            Fingerprint: $"protocol|{ruleId}|{component}|{violation.Description}");
    }

    private static SarifEntry CreateSarifEntry(SecurityVulnerability vulnerability)
    {
        var ruleId = string.IsNullOrWhiteSpace(vulnerability.Id)
            ? BuildFallbackRuleId("MCP.SECURITY", vulnerability.Category, vulnerability.Name, vulnerability.AffectedComponent)
            : vulnerability.Id;

        var component = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent)
            ? (string.IsNullOrWhiteSpace(vulnerability.Category) ? "security" : vulnerability.Category)
            : vulnerability.AffectedComponent;

        var properties = new Dictionary<string, object?>
        {
            ["source"] = "security-vulnerability",
            ["authority"] = ValidationRuleSourceClassifier.GetLabel(vulnerability),
            ["category"] = vulnerability.Category,
            ["component"] = component,
            ["severity"] = vulnerability.Severity.ToString(),
            ["isExploitable"] = vulnerability.IsExploitable
        };

        if (vulnerability.CvssScore.HasValue)
        {
            properties["cvssScore"] = vulnerability.CvssScore.Value;
        }

        if (!string.IsNullOrWhiteSpace(vulnerability.Remediation))
        {
            properties["recommendation"] = vulnerability.Remediation;
        }

        if (!string.IsNullOrWhiteSpace(vulnerability.ProofOfConcept))
        {
            properties["proofOfConcept"] = vulnerability.ProofOfConcept;
        }

        return new SarifEntry(
            RuleId: ruleId,
            Category: string.IsNullOrWhiteSpace(vulnerability.Category) ? "SecurityTesting" : vulnerability.Category,
            Component: component,
            Level: MapVulnerabilityLevel(vulnerability.Severity),
            Message: vulnerability.Description,
            ShortDescription: vulnerability.Name,
            FullDescription: vulnerability.Description,
            Recommendation: vulnerability.Remediation,
            HelpUri: null,
            Properties: properties,
                Source: ValidationRuleSourceClassifier.GetLabel(vulnerability),
            Fingerprint: $"vuln|{ruleId}|{component}|{vulnerability.Description}");
    }

    private static string MapFindingLevel(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => "error",
            ValidationFindingSeverity.High => "error",
            ValidationFindingSeverity.Medium => "warning",
            ValidationFindingSeverity.Low => "note",
            _ => "note"
        };
    }

    private static string MapViolationLevel(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => "error",
            ViolationSeverity.High => "error",
            ViolationSeverity.Medium => "warning",
            ViolationSeverity.Low => "note",
            _ => "note"
        };
    }

    private static string MapVulnerabilityLevel(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "error",
            VulnerabilitySeverity.High => "error",
            VulnerabilitySeverity.Medium => "warning",
            VulnerabilitySeverity.Low => "note",
            VulnerabilitySeverity.Informational => "note",
            _ => "note"
        };
    }

    private static string[] BuildSarifTags(string source, string category, string component)
    {
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(source))
        {
            tags.Add(source);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            tags.Add(category);
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            tags.Add(component);
        }

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? TryGetHelpUri(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("specReference", out var specReference) &&
            Uri.TryCreate(specReference, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || absoluteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri.ToString();
        }

        return null;
    }

    private static string BuildFallbackRuleId(params string?[] parts)
    {
        var normalized = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => new string(part!
                .Trim()
                .ToUpperInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray()).Trim('_'))
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var joined = string.Join('.', normalized);
        return string.IsNullOrWhiteSpace(joined) ? "MCP.UNKNOWN" : joined;
    }

    private sealed record SarifEntry(
        string RuleId,
        string Category,
        string Component,
        string Level,
        string Message,
        string ShortDescription,
        string FullDescription,
        string? Recommendation,
        string? HelpUri,
        Dictionary<string, object?> Properties,
        string Source,
        string Fingerprint);

    private static IReadOnlyList<JunitTestCase> BuildJunitTestCases(ValidationResult validationResult)
    {
        var testCases = new List<JunitTestCase>
        {
            CreateOverallJunitTestCase(validationResult)
        };

        if (validationResult.PolicyOutcome != null)
        {
            testCases.Add(CreatePolicyJunitTestCase(validationResult.PolicyOutcome, validationResult));
        }

        AddCategoryJunitTestCase(testCases, "protocol-compliance", "Protocol Compliance", validationResult.ProtocolCompliance, BuildProtocolDetails(validationResult.ProtocolCompliance));
        AddCategoryJunitTestCase(testCases, "tool-validation", "Tool Validation", validationResult.ToolValidation, BuildToolDetails(validationResult.ToolValidation));
        AddCategoryJunitTestCase(testCases, "resource-testing", "Resource Testing", validationResult.ResourceTesting, BuildResourceDetails(validationResult.ResourceTesting));
        AddCategoryJunitTestCase(testCases, "prompt-testing", "Prompt Testing", validationResult.PromptTesting, BuildPromptDetails(validationResult.PromptTesting));
        AddCategoryJunitTestCase(testCases, "security-testing", "Security Testing", validationResult.SecurityTesting, BuildSecurityDetails(validationResult.SecurityTesting));
        AddCategoryJunitTestCase(testCases, "performance-testing", "Performance Testing", validationResult.PerformanceTesting, BuildPerformanceDetails(validationResult.PerformanceTesting));
        AddCategoryJunitTestCase(testCases, "error-handling", "Error Handling", validationResult.ErrorHandling, BuildErrorHandlingDetails(validationResult.ErrorHandling));

        return testCases;
    }

    private static JunitTestCase CreateOverallJunitTestCase(ValidationResult validationResult)
    {
        var details = new List<string>
        {
            $"Compliance Score: {validationResult.ComplianceScore:F1}%",
            $"Summary: total={validationResult.Summary.TotalTests}, passed={validationResult.Summary.PassedTests}, failed={validationResult.Summary.FailedTests}, skipped={validationResult.Summary.SkippedTests}",
            $"Endpoint: {validationResult.ServerConfig.Endpoint}",
            $"Transport: {validationResult.ServerConfig.Transport}"
        };

        if (validationResult.CriticalErrors.Count > 0)
        {
            details.AddRange(validationResult.CriticalErrors.Select(error => $"Critical Error: {error}"));
        }

        if (validationResult.Recommendations.Count > 0)
        {
            details.AddRange(validationResult.Recommendations.Select(recommendation => $"Recommendation: {recommendation}"));
        }

        return CreateJunitTestCase(
            suiteName: "overall-validation",
            className: "validation.overall",
            name: "Overall Validation Run",
            status: MapValidationStatus(validationResult.OverallStatus),
            duration: validationResult.Duration ?? TimeSpan.Zero,
            message: $"Overall status: {validationResult.OverallStatus}",
            details: string.Join(Environment.NewLine, details));
    }

    private static JunitTestCase CreatePolicyJunitTestCase(ValidationPolicyOutcome policyOutcome, ValidationResult validationResult)
    {
        var details = new List<string>
        {
            policyOutcome.Summary,
            $"Recommended Exit Code: {policyOutcome.RecommendedExitCode}",
            $"Suppressed Signals: {policyOutcome.SuppressedSignalCount}"
        };

        if (policyOutcome.Reasons.Count > 0)
        {
            details.AddRange(policyOutcome.Reasons.Select(reason => $"Reason: {reason}"));
        }

        if (policyOutcome.AppliedSuppressions.Count > 0)
        {
            details.AddRange(policyOutcome.AppliedSuppressions.Select(suppression => $"Applied Suppression: {suppression.Id} by {suppression.Owner} ({suppression.MatchedSignalCount} signal(s))"));
        }

        if (policyOutcome.IgnoredSuppressions.Count > 0)
        {
            details.AddRange(policyOutcome.IgnoredSuppressions.Select(suppression => $"Ignored Suppression: {suppression.Id} - {suppression.Reason}"));
        }

        return CreateJunitTestCase(
            suiteName: "host-policy",
            className: "validation.policy",
            name: $"Policy Gate ({policyOutcome.Mode})",
            status: policyOutcome.Passed ? TestStatus.Passed : TestStatus.Failed,
            duration: validationResult.Duration ?? TimeSpan.Zero,
            message: policyOutcome.Summary,
            details: string.Join(Environment.NewLine, details));
    }

    private static void AddCategoryJunitTestCase(
        ICollection<JunitTestCase> testCases,
        string suiteName,
        string name,
        TestResultBase? result,
        string? details)
    {
        if (result == null)
        {
            return;
        }

        testCases.Add(CreateJunitTestCase(
            suiteName: suiteName,
            className: $"validation.{suiteName.Replace('-', '.')}",
            name: name,
            status: result.Status,
            duration: result.Duration,
            message: result.Message ?? $"{name} finished with status {result.Status}.",
            details: details));
    }

    private static JunitTestCase CreateJunitTestCase(
        string suiteName,
        string className,
        string name,
        TestStatus status,
        TimeSpan duration,
        string message,
        string? details)
    {
        var normalizedDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        return status switch
        {
            TestStatus.Passed => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Passed, message, null, normalizedDetails),
            TestStatus.Skipped => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Skipped, message, normalizedDetails, normalizedDetails),
            TestStatus.Error or TestStatus.Cancelled or TestStatus.InProgress => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Error, message, normalizedDetails, normalizedDetails),
            _ => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Failure, message, normalizedDetails, normalizedDetails)
        };
    }

    private static TestStatus MapValidationStatus(ValidationStatus status)
    {
        return status switch
        {
            ValidationStatus.Passed => TestStatus.Passed,
            ValidationStatus.Failed => TestStatus.Failed,
            _ => TestStatus.Error
        };
    }

    private static string? BuildProtocolDetails(ComplianceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result, $"Compliance Score: {result.ComplianceScore:F1}%");
        lines.AddRange(result.Violations.Select(violation => $"Violation [{violation.Severity}] {violation.CheckId ?? violation.Rule}: {violation.Description}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildToolDetails(ToolTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Score: {result.Score:F1}%",
            $"Tools Discovered: {result.ToolsDiscovered}",
            $"Tool Pass Count: {result.ToolsTestPassed}",
            $"Tool Fail Count: {result.ToolsTestFailed}");
        lines.AddRange(result.ToolResults.Select(tool => $"Tool {tool.ToolName}: {tool.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildResourceDetails(ResourceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Resources Discovered: {result.ResourcesDiscovered}",
            $"Resources Accessible: {result.ResourcesAccessible}",
            $"Resources Failed: {result.ResourcesTestFailed}");
        lines.AddRange(result.ResourceResults.Select(resource => $"Resource {resource.ResourceName ?? resource.ResourceUri}: {resource.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildPromptDetails(PromptTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Prompts Discovered: {result.PromptsDiscovered}",
            $"Prompts Passed: {result.PromptsTestPassed}",
            $"Prompts Failed: {result.PromptsTestFailed}");
        lines.AddRange(result.PromptResults.Select(prompt => $"Prompt {prompt.PromptName}: {prompt.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildSecurityDetails(SecurityTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result, $"Security Score: {result.SecurityScore:F1}%");
        lines.AddRange(result.Vulnerabilities.Select(vulnerability => $"Vulnerability [{vulnerability.Severity}] {vulnerability.Id}: {vulnerability.Description}"));
        lines.AddRange(result.SecurityRecommendations.Select(recommendation => $"Recommendation: {recommendation}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildPerformanceDetails(PerformanceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Average Latency Ms: {result.LoadTesting.AverageResponseTimeMs:F2}",
            $"P95 Latency Ms: {result.LoadTesting.P95ResponseTimeMs:F2}",
            $"Requests Per Second: {result.LoadTesting.RequestsPerSecond:F2}",
            $"Error Rate: {result.LoadTesting.ErrorRate:F2}");
        lines.AddRange(result.PerformanceBottlenecks.Select(bottleneck => $"Bottleneck: {bottleneck}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildErrorHandlingDetails(ErrorHandlingTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Error Scenarios Tested: {result.ErrorScenariosTestCount}",
            $"Error Scenarios Handled Correctly: {result.ErrorScenariosHandledCorrectly}");
        lines.AddRange(result.ErrorScenarioResults.Select(error =>
            $"Scenario {error.ScenarioName}: {(error.HandledCorrectly ? "Handled correctly" : "Handling failed")}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static List<string> BuildCommonDetailLines(TestResultBase result, params string[] additionalLines)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            lines.Add($"Message: {result.Message}");
        }

        lines.AddRange(additionalLines.Where(line => !string.IsNullOrWhiteSpace(line)));
        lines.AddRange(result.CriticalErrors.Select(error => $"Critical Error: {error}"));
        lines.AddRange(result.Findings.Select(finding => $"Finding [{finding.EffectiveSourceLabel}/{finding.Severity}] {finding.RuleId}: {finding.Summary}"));
        return lines;
    }

    private enum JunitOutcome
    {
        Passed,
        Failure,
        Error,
        Skipped
    }

    private sealed record JunitTestCase(
        string SuiteName,
        string ClassName,
        string Name,
        double TimeSeconds,
        JunitOutcome Outcome,
        string Message,
        string? Details,
        string? SystemOut);
}
