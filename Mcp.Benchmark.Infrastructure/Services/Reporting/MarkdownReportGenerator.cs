using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

/// <summary>
/// Generates professional Markdown reports for validation results.
/// </summary>
public class MarkdownReportGenerator : IReportGenerator
{
    public string GenerateReport(ValidationResult result)
    {
        var sb = new StringBuilder();
        var sectionNumber = 1;

        // Header
        sb.AppendLine("# MCP Server Compliance & Validation Report");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Executive Summary
        sb.AppendLine($"## {sectionNumber++}. Executive Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Server Endpoint** | `{result.ServerConfig.Endpoint}` |");
        sb.AppendLine($"| **Validation ID** | `{result.ValidationId}` |");
        sb.AppendLine($"| **Overall Status** | {GetStatusIcon(result.OverallStatus)} **{result.OverallStatus}** |");
        sb.AppendLine($"| **Compliance Score** | **{result.ComplianceScore:F1}%** |");
        sb.AppendLine($"| **Compliance Profile** | `{FormatProfileLabel(result)}` |");
        sb.AppendLine($"| **Duration** | {result.Duration?.TotalSeconds:F2}s |");
        sb.AppendLine($"| **Transport** | {result.ServerConfig.Transport?.ToUpper() ?? "HTTP"} |");
        if (!string.IsNullOrWhiteSpace(result.ProtocolVersion))
        {
            sb.AppendLine($"| **MCP Protocol Version (Effective)** | `{result.ProtocolVersion}` |");
        }
        if (result.TrustAssessment != null)
        {
            var trustIcon = result.TrustAssessment.TrustLevel switch
            {
                McpTrustLevel.L5_CertifiedSecure => "🟢",
                McpTrustLevel.L4_Trusted => "🔵",
                McpTrustLevel.L3_Acceptable => "🟡",
                McpTrustLevel.L2_Caution => "🟠",
                _ => "🔴"
            };
            sb.AppendLine($"| **MCP Trust Level** | {trustIcon} **{result.TrustAssessment.TrustLabel}** |");
        }
        sb.AppendLine();

        // MCP Trust Assessment (before compliance matrix)
        if (result.TrustAssessment != null)
        {
            sb.AppendLine($"## {sectionNumber++}. MCP Trust Assessment");
            sb.AppendLine();
            sb.AppendLine("Multi-dimensional evaluation of server trustworthiness for AI agent consumption.");
            sb.AppendLine("Trust level is determined by the **weakest dimension** (security-first principle).");
            sb.AppendLine();
            sb.AppendLine("| Dimension | Score | What It Measures |");
            sb.AppendLine("| :--- | :---: | :--- |");
            sb.AppendLine($"| **Protocol Compliance** | {result.TrustAssessment.ProtocolCompliance:F0}% | MCP spec adherence, JSON-RPC compliance, response structures |");
            sb.AppendLine($"| **Security Posture** | {result.TrustAssessment.SecurityPosture:F0}% | Auth compliance, injection resistance, attack surface |");
            sb.AppendLine($"| **AI Safety** | {result.TrustAssessment.AiSafety:F0}% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |");
            sb.AppendLine($"| **Operational Readiness** | {result.TrustAssessment.OperationalReadiness:F0}% | Latency, throughput, error rate, stability |");
            if (result.TrustAssessment.LlmFriendlinessScore >= 0)
            {
                var llmGrade = result.TrustAssessment.LlmFriendlinessScore >= 70 ? "🟢 Pro-LLM" : result.TrustAssessment.LlmFriendlinessScore >= 40 ? "🟡 Neutral" : "🔴 Anti-LLM";
                sb.AppendLine($"| **LLM-Friendliness** | {result.TrustAssessment.LlmFriendlinessScore:F0}% | {llmGrade} — Do error responses help AI agents self-correct? |");
            }
            sb.AppendLine();

            if (result.TrustAssessment.BoundaryFindings.Count > 0)
            {
                sb.AppendLine("### AI Boundary Findings");
                sb.AppendLine();
                sb.AppendLine("These findings go **beyond MCP protocol** to assess how AI agents interact with this server.");
                sb.AppendLine();
                sb.AppendLine("| Category | Component | Severity | Finding |");
                sb.AppendLine("| :--- | :--- | :---: | :--- |");
                foreach (var finding in result.TrustAssessment.BoundaryFindings)
                {
                    var sevIcon = finding.Severity switch { "Critical" => "🔴", "High" => "🟠", "Medium" => "🟡", _ => "🔵" };
                    sb.AppendLine($"| {finding.Category} | `{finding.Component}` | {sevIcon} {finding.Severity} | {finding.Description} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("### Summary");
            sb.AppendLine();
            sb.AppendLine($"- Destructive tools: **{result.TrustAssessment.DestructiveToolCount}**");
            sb.AppendLine($"- Data exfiltration risk: **{result.TrustAssessment.DataExfiltrationRiskCount}**");
            sb.AppendLine($"- Prompt injection surface: **{result.TrustAssessment.PromptInjectionSurfaceCount}**");
            sb.AppendLine();

            // Compliance Tiers Table (MUST/SHOULD/MAY)
            if (result.TrustAssessment.TierChecks.Count > 0)
            {
                sb.AppendLine("### MCP Spec Compliance (RFC 2119 Tiers)");
                sb.AppendLine();
                sb.AppendLine($"| Tier | Passed | Failed | Total | Impact |");
                sb.AppendLine($"| :--- | :---: | :---: | :---: | :--- |");
                sb.AppendLine($"| **MUST** | {result.TrustAssessment.MustPassCount} | {result.TrustAssessment.MustFailCount} | {result.TrustAssessment.MustTotalCount} | {(result.TrustAssessment.MustFailCount > 0 ? "❌ Non-compliant (trust capped at L2)" : "✅ Fully compliant")} |");
                sb.AppendLine($"| **SHOULD** | {result.TrustAssessment.ShouldPassCount} | {result.TrustAssessment.ShouldFailCount} | {result.TrustAssessment.ShouldTotalCount} | {(result.TrustAssessment.ShouldFailCount > 0 ? $"⚠️ {result.TrustAssessment.ShouldFailCount} penalties applied" : "✅ All expected behaviors present")} |");
                sb.AppendLine($"| **MAY** | {result.TrustAssessment.MaySupported} | {result.TrustAssessment.MayTotal - result.TrustAssessment.MaySupported} | {result.TrustAssessment.MayTotal} | ℹ️ Informational (no score impact) |");
                sb.AppendLine();

                // Show failed MUST checks prominently
                var mustFails = result.TrustAssessment.TierChecks.Where(c => c.Tier == "MUST" && !c.Passed).ToList();
                if (mustFails.Count > 0)
                {
                    sb.AppendLine("#### ❌ MUST Failures (Compliance Blockers)");
                    sb.AppendLine();
                    foreach (var fail in mustFails)
                    {
                        sb.AppendLine($"- **{fail.Requirement}** ({fail.Component}){(fail.Detail != null ? $" — {fail.Detail}" : "")}");
                    }
                    sb.AppendLine();
                }

                // Show SHOULD failures as warnings
                var shouldFails = result.TrustAssessment.TierChecks.Where(c => c.Tier == "SHOULD" && !c.Passed).ToList();
                if (shouldFails.Count > 0)
                {
                    sb.AppendLine("#### ⚠️ SHOULD Gaps (Score Penalties)");
                    sb.AppendLine();
                    foreach (var fail in shouldFails)
                    {
                        sb.AppendLine($"- {fail.Requirement} ({fail.Component})");
                    }
                    sb.AppendLine();
                }

                // Show MAY features as informational
                var mayChecks = result.TrustAssessment.TierChecks.Where(c => c.Tier == "MAY").ToList();
                if (mayChecks.Count > 0)
                {
                    sb.AppendLine("#### ℹ️ Optional Features (MAY)");
                    sb.AppendLine();
                    foreach (var check in mayChecks)
                    {
                        var icon = check.Passed ? "✅" : "➖";
                        sb.AppendLine($"- {icon} {check.Requirement}");
                    }
                    sb.AppendLine();
                }
            }
        }

        AppendCapabilitySnapshotSection(sb, result, ref sectionNumber);

        // Compliance Matrix
        sb.AppendLine($"## {sectionNumber++}. Compliance Matrix");
        sb.AppendLine();
        sb.AppendLine("| Category | Status | Score | Issues |");
        sb.AppendLine("| :--- | :---: | :---: | :---: |");
        
        AddMatrixRow(sb, "Protocol Compliance", result.ProtocolCompliance?.Status, result.ProtocolCompliance?.ComplianceScore, result.ProtocolCompliance?.Violations?.Count);
        AddMatrixRow(sb, "Security Assessment", result.SecurityTesting?.Status, result.SecurityTesting?.SecurityScore, result.SecurityTesting?.Vulnerabilities?.Count);
        AddMatrixRow(sb, "Tool Validation", result.ToolValidation?.Status, result.ToolValidation?.Score, result.ToolValidation?.ToolsTestFailed);
        AddMatrixRow(sb, "Resource Capabilities", result.ResourceTesting?.Status, result.ResourceTesting?.Score, result.ResourceTesting?.ResourcesTestFailed);
        AddMatrixRow(sb, "Prompt Capabilities", result.PromptTesting?.Status, result.PromptTesting?.Score, result.PromptTesting?.PromptsTestFailed);
        AddMatrixRow(sb, "Performance", result.PerformanceTesting?.Status, result.PerformanceTesting?.Score, 0); // Perf usually doesn't have "issues" count in same way
        sb.AppendLine();

        // Detailed Findings - Security
        if (result.SecurityTesting != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Security Assessment");
            sb.AppendLine();
            sb.AppendLine($"**Security Score:** {result.SecurityTesting.SecurityScore:F1}%");
            sb.AppendLine();
            
            if (result.SecurityTesting.AuthenticationTestResult?.TestScenarios?.Any() == true)
            {
                sb.AppendLine("### Authentication Analysis");
                sb.AppendLine("| Scenario | Method | Expected | Actual | Analysis | Status |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :---: |");
                foreach (var test in result.SecurityTesting.AuthenticationTestResult.TestScenarios)
                {
                    sb.AppendLine($"| {test.ScenarioName} | `{test.Method}` | {test.ExpectedBehavior} | {test.ActualBehavior} | {test.Analysis} | {GetStatusIcon(test.IsCompliant ? TestStatus.Passed : TestStatus.Failed)} |");
                }
                sb.AppendLine();
            }

            if (result.SecurityTesting.AttackSimulations?.Any() == true)
            {
                sb.AppendLine("### Adversarial Input Handling");
                sb.AppendLine();
                sb.AppendLine("> **Legend:**");
                sb.AppendLine("> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.");
                sb.AppendLine("> * **BLOCKED**: The server correctly rejected or sanitized the input.");
                sb.AppendLine("> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).");
                sb.AppendLine();
                sb.AppendLine("| Attack Vector | Description | Result | Analysis |");
                sb.AppendLine("| :--- | :--- | :---: | :--- |");
                foreach (var attack in result.SecurityTesting.AttackSimulations)
                {
                    var analysisText = attack.ServerResponse ?? "";
                    var isSkipped = analysisText.Contains("Skipped", StringComparison.OrdinalIgnoreCase);
                    string status;
                    if (isSkipped) status = "⏭️ SKIPPED";
                    else if (attack.DefenseSuccessful) status = "🛡️ BLOCKED";
                    else status = "⚠️ REFLECTED / UNSAFE ECHO";

                    var response = string.IsNullOrEmpty(attack.ServerResponse) 
                        ? "-" 
                        : $"`{attack.ServerResponse.Replace("|", "\\|").Replace("\n", " ")}`";
                    
                    sb.AppendLine($"| {attack.AttackVector} | {attack.Description} | {status} | {response} |");
                }
                sb.AppendLine();
            }
        }

        // Detailed Findings - Protocol
        if (result.ProtocolCompliance != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Protocol Compliance");
            sb.AppendLine();
            
            if (result.ProtocolCompliance.Violations?.Any() == true)
            {
                sb.AppendLine("### Compliance Violations");
                sb.AppendLine("| ID | Description | Severity |");
                sb.AppendLine("| :--- | :--- | :---: |");
                foreach (var issue in result.ProtocolCompliance.Violations)
                {
                    sb.AppendLine($"| {issue.CheckId} | {issue.Description} | {issue.Severity} |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("✅ No protocol violations detected.");
                sb.AppendLine();
            }
        }

        // Detailed Findings - Tools
        if (result.ToolValidation != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Tool Validation");
            sb.AppendLine();
            sb.AppendLine($"**Tools Discovered:** {result.ToolValidation.ToolsDiscovered}");

            if (result.ToolValidation.AuthenticationSecurity is { } authSummary)
            {
                var enforcementLabel = result.ToolValidation.AuthenticationProperlyEnforced ? "✅ Protected" : "⚠️ Not Enforced";
                sb.AppendLine($"**Authentication Enforcement:** {enforcementLabel}");
                if (!string.IsNullOrWhiteSpace(authSummary.WwwAuthenticateHeader))
                {
                    var sanitizedHeader = authSummary.WwwAuthenticateHeader.Replace("|", "\\|");
                    sb.AppendLine($"**WWW-Authenticate:** `{sanitizedHeader}`");
                }
                if (!string.IsNullOrWhiteSpace(authSummary.AuthMetadata?.Resource))
                {
                    var protectedResource = authSummary.AuthMetadata.Resource!.Replace("|", "\\|");
                    sb.AppendLine($"**Protected Resource:** `{protectedResource}`");
                }
            }

            sb.AppendLine();

            if (result.ToolValidation.ToolResults?.Any() == true)
            {
                foreach (var tool in result.ToolValidation.ToolResults)
                {
                    sb.AppendLine($"### Tool: `{tool.ToolName}`");
                    sb.AppendLine();
                    sb.AppendLine($"**Status:** {GetStatusIcon(tool.Status)} {tool.Status}");
                    sb.AppendLine($"**Execution Time:** {tool.ExecutionTimeMs:F2}ms");

                    if (!string.IsNullOrWhiteSpace(tool.DisplayTitle) ||
                        tool.ReadOnlyHint.HasValue ||
                        tool.DestructiveHint.HasValue ||
                        tool.OpenWorldHint.HasValue ||
                        tool.IdempotentHint.HasValue)
                    {
                        sb.AppendLine();
                        sb.AppendLine("#### Tool Metadata");
                        sb.AppendLine("| Property | Value |");
                        sb.AppendLine("| :--- | :--- |");
                        if (!string.IsNullOrWhiteSpace(tool.DisplayTitle)) sb.AppendLine($"| Display Title | {tool.DisplayTitle} |");
                        if (tool.ReadOnlyHint.HasValue) sb.AppendLine($"| readOnlyHint | {tool.ReadOnlyHint.Value} |");
                        if (tool.DestructiveHint.HasValue) sb.AppendLine($"| destructiveHint | {tool.DestructiveHint.Value} |");
                        if (tool.OpenWorldHint.HasValue) sb.AppendLine($"| openWorldHint | {tool.OpenWorldHint.Value} |");
                        if (tool.IdempotentHint.HasValue) sb.AppendLine($"| idempotentHint | {tool.IdempotentHint.Value} |");
                        sb.AppendLine();
                    }
                    
                    if (tool.AuthMetadata != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("#### Authentication Metadata");
                        sb.AppendLine("| Property | Value |");
                        sb.AppendLine("| :--- | :--- |");
                        if (tool.AuthMetadata.Resource != null) sb.AppendLine($"| Resource | `{tool.AuthMetadata.Resource}` |");
                        if (tool.AuthMetadata.AuthorizationServers?.Any() == true) sb.AppendLine($"| Auth Servers | {string.Join(", ", tool.AuthMetadata.AuthorizationServers)} |");
                        if (tool.AuthMetadata.ScopesSupported?.Any() == true) sb.AppendLine($"| Scopes | {string.Join(", ", tool.AuthMetadata.ScopesSupported)} |");
                        if (tool.AuthMetadata.BearerMethodsSupported?.Any() == true) sb.AppendLine($"| Bearer Methods | {string.Join(", ", tool.AuthMetadata.BearerMethodsSupported)} |");
                        sb.AppendLine();
                    }

                    if (tool.WwwAuthenticateHeader != null)
                    {
                        sb.AppendLine($"**WWW-Authenticate Header:** `{tool.WwwAuthenticateHeader}`");
                        sb.AppendLine();
                    }

                    if (tool.ParameterTests?.Any() == true)
                    {
                        sb.AppendLine("#### Parameter Validation");
                        sb.AppendLine("| Parameter | Scenario | Expected | Actual | Result |");
                        sb.AppendLine("| :--- | :--- | :--- | :--- | :---: |");
                        foreach (var param in tool.ParameterTests)
                        {
                            sb.AppendLine($"| `{param.ParameterName}` | {param.TestScenario} | {param.ExpectedBehavior} | {param.ActualBehavior} | {(param.ValidationPassed ? "✅" : "❌")} |");
                        }
                        sb.AppendLine();
                    }
                    
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                var guidelineFindings = result.ToolValidation.ToolResults
                    .SelectMany(tool => tool.Findings.Where(f => f.Category == "McpGuideline"))
                    .ToList();

                if (guidelineFindings.Count > 0)
                {
                    sb.AppendLine("### MCP Guideline Findings");
                    sb.AppendLine();
                    sb.AppendLine("These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.");
                    sb.AppendLine();
                    sb.AppendLine("| Rule ID | Tool | Severity | Finding |");
                    sb.AppendLine("| :--- | :--- | :---: | :--- |");
                    foreach (var finding in guidelineFindings)
                    {
                        var severityIcon = finding.Severity switch
                        {
                            ValidationFindingSeverity.Critical => "🔴 Critical",
                            ValidationFindingSeverity.High => "🟠 High",
                            ValidationFindingSeverity.Medium => "🟡 Medium",
                            ValidationFindingSeverity.Low => "🔵 Low",
                            _ => "⚪ Info"
                        };
                        sb.AppendLine($"| `{finding.RuleId}` | `{finding.Component}` | {severityIcon} | {finding.Summary} |");
                    }
                    sb.AppendLine();
                }
            }
        }

        // Detailed Findings - Resources
        if (result.ResourceTesting != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Resource Capabilities");
            sb.AppendLine();
            sb.AppendLine($"**Resources Discovered:** {result.ResourceTesting.ResourcesDiscovered}");
            sb.AppendLine();

            if (result.ResourceTesting.ResourceResults?.Any() == true)
            {
                sb.AppendLine("| Resource Name | URI | MIME Type | Size | Status |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :---: |");
                foreach (var res in result.ResourceTesting.ResourceResults)
                {
                    sb.AppendLine($"| {res.ResourceName} | `{res.ResourceUri}` | {res.MimeType ?? "-"} | {res.ContentSize?.ToString() ?? "-"} | {GetStatusIcon(res.Status)} |");
                }
                sb.AppendLine();
            }
        }

        // AI Readiness Assessment
        if (result.ToolValidation != null && result.ToolValidation.AiReadinessScore >= 0)
        {
            sb.AppendLine($"## {sectionNumber++}. AI Readiness Assessment");
            sb.AppendLine();
            var aiScore = result.ToolValidation.AiReadinessScore;
            var grade = aiScore >= 80 ? "Good" : aiScore >= 50 ? "Fair" : "Poor";
            var gradeIcon = aiScore >= 80 ? "✅" : aiScore >= 50 ? "⚠️" : "❌";
            sb.AppendLine($"**AI Readiness Score:** {gradeIcon} **{aiScore:F0}/100** ({grade})");
            sb.AppendLine();
            sb.AppendLine("This score measures how well the server's tool schemas are optimized for consumption by AI agents (LLMs).");
            sb.AppendLine();
            sb.AppendLine("| Criterion | What It Measures |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine("| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |");
            sb.AppendLine("| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |");
            sb.AppendLine($"| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~{result.ToolValidation.EstimatedTokenCount:N0} tokens. |");
            sb.AppendLine();

            if (result.ToolValidation.AiReadinessIssues?.Any() == true)
            {
                sb.AppendLine("### Findings");
                sb.AppendLine();
                foreach (var issue in result.ToolValidation.AiReadinessIssues)
                {
                    sb.AppendLine($"- {issue}");
                }
                sb.AppendLine();
            }
        }

        // Protocol Capability Probes
        if (result.ProtocolCompliance?.Message != null && result.ProtocolCompliance.Message.Contains("|"))
        {
            sb.AppendLine($"## {sectionNumber++}. Optional MCP Capabilities");
            sb.AppendLine();
            sb.AppendLine("These probes check whether the server supports optional MCP features beyond the core primitives.");
            sb.AppendLine();
            sb.AppendLine("| Capability | Status |");
            sb.AppendLine("| :--- | :--- |");
            var probes = result.ProtocolCompliance.Message.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var probe in probes)
            {
                var parts = probe.Split(':', 2);
                if (parts.Length == 2)
                {
                    var name = parts[0].Trim();
                    var status = parts[1].Trim();
                    var icon = status.Contains("supported", StringComparison.OrdinalIgnoreCase) && !status.Contains("not") ? "✅" : "➖";
                    sb.AppendLine($"| `{name}` | {icon} {status} |");
                }
            }
            sb.AppendLine();
        }

        // Performance
        if (result.PerformanceTesting != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Performance Metrics");
            sb.AppendLine();

            if (result.PerformanceTesting.Status == TestStatus.Skipped)
            {
                sb.AppendLine($"**Status:** ➖ Skipped");
                sb.AppendLine($"**Reason:** {result.PerformanceTesting.Message ?? "Performance testing was skipped (authentication required or not configured)."}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("| Metric | Result | Verdict |");
                sb.AppendLine("| :--- | :--- | :--- |");
                sb.AppendLine($"| **Avg Latency** | {result.PerformanceTesting.LoadTesting.AverageResponseTimeMs:F2}ms | {GetPerformanceVerdict(result.PerformanceTesting.LoadTesting.AverageResponseTimeMs)} |");
                sb.AppendLine($"| **P95 Latency** | {result.PerformanceTesting.LoadTesting.P95ResponseTimeMs:F2}ms | - |");
                sb.AppendLine($"| **Throughput** | {result.PerformanceTesting.LoadTesting.RequestsPerSecond:F2} req/sec | - |");
                sb.AppendLine($"| **Error Rate** | {result.PerformanceTesting.LoadTesting.ErrorRate:F2}% | {(result.PerformanceTesting.LoadTesting.ErrorRate > 0 ? "⚠️ Check Logs" : "✅ Clean")} |");
                sb.AppendLine();
            }
        }

        // Recommendations
        if (result.Recommendations?.Any() == true)
        {
            sb.AppendLine($"## {sectionNumber++}. Recommendations");
            sb.AppendLine();
            foreach (var rec in result.Recommendations)
            {
                sb.AppendLine($"- 💡 {rec}");
            }
            sb.AppendLine();
        }

        // Footer
        sb.AppendLine("---");
        sb.AppendLine("*Report generated by mcpval — MCP Validator*");

        return sb.ToString();
    }

    private static string FormatProfileLabel(ValidationResult result)
    {
        if (result.ServerProfile == McpServerProfile.Unspecified)
        {
            return "unspecified";
        }

        var profile = result.ServerProfile.ToString();
        var sourceSuffix = result.ServerProfileSource != ServerProfileSource.Unknown
            ? $" ({result.ServerProfileSource})"
            : string.Empty;

        return profile + sourceSuffix;
    }

    private void AppendCapabilitySnapshotSection(StringBuilder sb, ValidationResult result, ref int sectionNumber)
    {
        if (result.CapabilitySnapshot?.Payload is not CapabilitySummary snapshot)
        {
            return;
        }

        sb.AppendLine($"## {sectionNumber++}. Capability Snapshot");
        sb.AppendLine();
        sb.AppendLine("| Probe | Discovered | HTTP Status | Duration | Result |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :--- |");

        var toolResult = FormatProbeResult(snapshot.ToolListingSucceeded, snapshot.ToolListResponse?.StatusCode ?? 0, "Listed");
        if (!string.IsNullOrWhiteSpace(snapshot.FirstToolName))
        {
            var callIcon = snapshot.ToolInvocationSucceeded ? "✅" : "⚠️";
            toolResult += $"<br/>Call {callIcon}";
        }

        sb.AppendLine($"| Tools/list | {snapshot.DiscoveredToolsCount} | {FormatHttpStatus(snapshot.ToolListResponse?.StatusCode)} | {FormatDuration(snapshot.ToolListDurationMs)} | {toolResult} |");
        sb.AppendLine($"| Resources/list | {snapshot.DiscoveredResourcesCount} | {FormatHttpStatus(snapshot.ResourceListResponse?.StatusCode)} | {FormatDuration(snapshot.ResourceListDurationMs)} | {FormatProbeResult(snapshot.ResourceListingSucceeded, snapshot.ResourceListResponse?.StatusCode ?? 0, "Listed")} |");
        sb.AppendLine($"| Prompts/list | {snapshot.DiscoveredPromptsCount} | {FormatHttpStatus(snapshot.PromptListResponse?.StatusCode)} | {FormatDuration(snapshot.PromptListDurationMs)} | {FormatProbeResult(snapshot.PromptListingSucceeded, snapshot.PromptListResponse?.StatusCode ?? 0, "Listed")} |");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(snapshot.FirstToolName))
        {
            sb.AppendLine($"- **First Tool Probed:** `{snapshot.FirstToolName}`");
        }

        sb.AppendLine();
    }

    private void AddMatrixRow(StringBuilder sb, string category, TestStatus? status, double? score, int? issues)
    {
        if (status == null) return;
        
        var statusStr = status == TestStatus.Skipped ? "Skipped" : status.ToString();
        var scoreStr = status == TestStatus.Skipped ? "-" : $"{score:F1}%";
        var issuesStr = issues.HasValue && issues > 0 ? $"**{issues}**" : "-";
        var icon = GetStatusIcon(status.Value);

        sb.AppendLine($"| {category} | {icon} {statusStr} | {scoreStr} | {issuesStr} |");
    }

    private string GetStatusIcon(ValidationStatus status) => status switch
    {
        ValidationStatus.Passed => "✅",
        ValidationStatus.Failed => "❌",
        _ => "ℹ️"
    };

    private string GetStatusIcon(TestStatus status) => status switch
    {
        TestStatus.Passed => "✅",
        TestStatus.Failed => "❌",
        TestStatus.Skipped => "➖",
        _ => "ℹ️"
    };

    private string GetPerformanceVerdict(double latency)
    {
        if (latency < 100) return "🚀 Excellent";
        if (latency < 500) return "✅ Good";
        if (latency < 1000) return "⚠️ Acceptable";
        return "❌ Slow";
    }

    private string FormatProbeResult(bool succeeded, string successText, string failureText = "Failed")
    {
        return succeeded
            ? $"{GetStatusIcon(ValidationStatus.Passed)} {successText}"
            : $"{GetStatusIcon(ValidationStatus.Failed)} {failureText}";
    }

    private string FormatProbeResult(bool succeeded, int statusCode, string successText)
    {
        if (succeeded) return $"{GetStatusIcon(ValidationStatus.Passed)} {successText}";
        if (statusCode == 401 || statusCode == 403) return "🔒 Auth Required";
        return $"{GetStatusIcon(ValidationStatus.Failed)} Failed";
    }

    private static string FormatDuration(double durationMs)
    {
        return durationMs > 0 ? $"{durationMs:F1} ms" : "-";
    }

    private static string FormatHttpStatus(int? statusCode)
    {
        return statusCode.HasValue ? $"HTTP {statusCode}" : "n/a";
    }
}
