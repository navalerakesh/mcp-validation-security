using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

/// <summary>
/// Renders detailed HTML and XML reports from validation results.
/// This is reusable across hosts (CLI, services, etc.).
/// </summary>
public class ValidationReportRenderer : IValidationReportRenderer
{
    public string GenerateHtmlReport(ValidationResult validationResult, ReportingConfig reportConfig, bool verbose)
    {
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
    <title>MCP Server Validation Report{(verbose ? " - Detailed" : "")}</title>
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
            <h1>MCP Server Validation Report{(verbose ? " - Detailed View" : "")}</h1>
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

            {(validationResult.CriticalErrors.Count > 0 ? $@"
            <div class=""section"">
                <h2>Critical Errors</h2>
                <div class=""error-list"">
                    {string.Join("", validationResult.CriticalErrors.Select(error => $"<div class=\"error-item\">• {error}</div>"))}
                </div>
            </div>" : "")}

            {(validationResult.Recommendations.Count > 0 ? $@"
            <div class=""section"">
                <h2>Recommendations</h2>
                <div class=""recommendation-list"">
                    {string.Join("", validationResult.Recommendations.Select(rec => $"<div class=\"recommendation-item\">• {rec}</div>"))}
                </div>
            </div>" : "")}

            {GenerateCapabilitySnapshotHtml(validationResult)}

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
            Generated by MCP Benchmark for MCP Servers{(verbose ? " - Detailed Report" : "")}
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

        var severityBreakdown = BuildSeverityBreakdownElement(validationResult);
        if (severityBreakdown != null)
        {
            reportElement.Add(severityBreakdown);
        }

        if (validationResult.CapabilitySnapshot?.Payload is CapabilitySummary snapshot)
        {
            reportElement.Add(BuildCapabilitySnapshotElement(snapshot));
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
            var perfElement = new XElement("PerformanceTesting",
                new XAttribute("status", perf.Status.ToString()),
                new XAttribute("durationSeconds", perf.Duration.TotalSeconds.ToString("F1"))
            );

            if (verbose && perf.LoadTesting != null)
            {
                var loadElement = new XElement("LoadTesting",
                    new XElement("AverageLatencyMs", perf.LoadTesting.AverageResponseTimeMs.ToString("F2")),
                    new XElement("P95LatencyMs", perf.LoadTesting.P95ResponseTimeMs.ToString("F2")),
                    new XElement("RequestsPerSecond", perf.LoadTesting.RequestsPerSecond.ToString("F2")),
                    new XElement("ErrorRate", perf.LoadTesting.ErrorRate.ToString("F2"))
                );
                perfElement.Add(loadElement);
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
                html.AppendLine("<div class=\"detailed-section\">");
                html.AppendLine($"                    <h4>Performance Metrics</h4>");
                html.AppendLine($"                    <p>Test Duration: {validationResult.PerformanceTesting.Duration.TotalSeconds:F1} seconds</p>");
                html.AppendLine($"                    <p>Status: {validationResult.PerformanceTesting.Status}</p>");
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
            sb.AppendLine($"    <table class=\"data-table rule-table\"><thead><tr><th align=\"left\">Rule</th><th align=\"left\">Severity</th><th align=\"left\">Category</th><th align=\"left\">Details</th>{specHeader}</tr></thead><tbody>");
            foreach (var violation in trimmed)
            {
                var ruleCell = BuildRuleIdentifierCell(violation.CheckId, violation.Rule);
                var severityBadge = BuildSeverityBadge(MapViolationSeverity(violation.Severity));
                var categoryCell = string.IsNullOrWhiteSpace(violation.Category)
                    ? "<span class=\"spec-placeholder\">—</span>"
                    : System.Net.WebUtility.HtmlEncode(violation.Category);
                var detailsCell = BuildDetailsCell(violation.Description, violation.Recommendation);
                var specCell = includeSpec ? $"<td>{BuildSpecReferenceCell(violation.SpecReference)}</td>" : string.Empty;
                sb.AppendLine($"      <tr><td>{ruleCell}</td><td>{severityBadge}</td><td>{categoryCell}</td><td>{detailsCell}</td>{specCell}</tr>");
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
            sb.AppendLine("    <table class=\"data-table rule-table\"><thead><tr><th align=\"left\">Issue</th><th align=\"left\">Severity</th><th align=\"left\">Component</th><th align=\"left\">Details</th></tr></thead><tbody>");
            foreach (var vulnerability in trimmedVulns)
            {
                var issueCell = BuildVulnerabilityIssueCell(vulnerability);
                var severityBadge = BuildSeverityBadge(MapVulnerabilitySeverity(vulnerability.Severity));
                var componentCell = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent)
                    ? "<span class=\"spec-placeholder\">—</span>"
                    : System.Net.WebUtility.HtmlEncode(vulnerability.AffectedComponent);
                var detailsCell = BuildDetailsCell(vulnerability.Description, vulnerability.Remediation);
                sb.AppendLine($"      <tr><td>{issueCell}</td><td>{severityBadge}</td><td>{componentCell}</td><td>{detailsCell}</td></tr>");
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
}
