using System.Globalization;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

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
        var bootstrapHealth = ResolveBootstrapHealth(result);
        var includeDetailedSections = ShouldIncludeDetailedSections(result);
        var actionHints = ReportActionHintBuilder.Build(result);

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
        if (bootstrapHealth != null)
        {
            sb.AppendLine($"| **Session Bootstrap** | {FormatHealthDispositionLabel(bootstrapHealth)} |");
            sb.AppendLine($"| **Deferred Validation** | {FormatDeferredValidationLabel(bootstrapHealth)} |");
        }
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

        AppendBootstrapSection(sb, result, bootstrapHealth, ref sectionNumber);
        AppendPriorityFindingsSection(sb, result, ref sectionNumber);
        AppendActionHintsSection(sb, actionHints, ref sectionNumber);

        // MCP Trust Assessment (before compliance matrix)
        if (result.TrustAssessment != null)
        {
            sb.AppendLine($"## {sectionNumber++}. MCP Trust Assessment");
            sb.AppendLine();
            sb.AppendLine("Multi-dimensional evaluation of server trustworthiness for AI agent consumption.");
            sb.AppendLine("Trust level is determined by a **weighted multi-dimensional score** and then capped by confirmed blockers such as critical security failures or MCP MUST failures.");
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

            if (includeDetailedSections && result.TrustAssessment.BoundaryFindings.Count > 0)
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
            var toolCatalogSize = ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation);
            sb.AppendLine($"- Destructive tools: **{FormatCoverage(result.TrustAssessment.DestructiveToolCount, toolCatalogSize)}**");
            sb.AppendLine($"- Data exfiltration risk: **{FormatCoverage(result.TrustAssessment.DataExfiltrationRiskCount, toolCatalogSize)}**");
            sb.AppendLine($"- Prompt injection surface: **{FormatCoverage(result.TrustAssessment.PromptInjectionSurfaceCount, toolCatalogSize)}**");
            sb.AppendLine();

            // Compliance Tiers Table (MUST/SHOULD/MAY)
            if (includeDetailedSections && result.TrustAssessment.TierChecks.Count > 0)
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

        AppendClientCompatibilitySection(sb, result, ref sectionNumber);

        AppendCapabilitySnapshotSection(sb, result, ref sectionNumber);

        if (includeDetailedSections && result.ScoringNotes?.Any() == true)
        {
            sb.AppendLine($"## {sectionNumber++}. Scoring Methodology");
            sb.AppendLine();
            sb.AppendLine("These notes explain how the overall score and blocking decision were calibrated for this run.");
            sb.AppendLine();
            foreach (var note in result.ScoringNotes)
            {
                sb.AppendLine($"- {note}");
            }
            sb.AppendLine();
        }

        // Compliance Matrix
        sb.AppendLine($"## {sectionNumber++}. Compliance Matrix");
        sb.AppendLine();
        sb.AppendLine("| Category | Status | Score | Issues |");
        sb.AppendLine("| :--- | :---: | :---: | :---: |");
        
        AddMatrixRow(sb, "Protocol Compliance", result.ProtocolCompliance?.Status, result.ProtocolCompliance?.ComplianceScore, GetProtocolIssueCount(result.ProtocolCompliance));
        AddMatrixRow(sb, "Security Assessment", result.SecurityTesting?.Status, result.SecurityTesting?.SecurityScore, result.SecurityTesting?.Vulnerabilities?.Count);
        AddMatrixRow(sb, "Tool Validation", result.ToolValidation?.Status, result.ToolValidation?.Score, result.ToolValidation?.ToolsTestFailed);
        AddMatrixRow(sb, "Resource Capabilities", result.ResourceTesting?.Status, result.ResourceTesting?.Score, result.ResourceTesting?.ResourcesTestFailed);
        AddMatrixRow(sb, "Prompt Capabilities", result.PromptTesting?.Status, result.PromptTesting?.Score, result.PromptTesting?.PromptsTestFailed);
        AddMatrixRow(sb, "Performance", result.PerformanceTesting?.Status, result.PerformanceTesting?.Score, 0, FormatPerformanceMatrixScore(result.PerformanceTesting)); // Perf usually doesn't have "issues" count in same way
        sb.AppendLine();

        if (includeDetailedSections && result.PerformanceTesting != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Performance Calibration");
            sb.AppendLine();
            sb.AppendLine("Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.");
            sb.AppendLine("For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.");
            sb.AppendLine();
            if (result.PerformanceTesting.Findings.Any())
            {
                foreach (var finding in result.PerformanceTesting.Findings)
                {
                    sb.AppendLine($"- {finding.Summary}");
                }
                sb.AppendLine();
            }
        }

        // Detailed Findings - Security
        if (includeDetailedSections && result.SecurityTesting != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Security Assessment");
            sb.AppendLine();
            sb.AppendLine($"**Security Score:** {result.SecurityTesting.SecurityScore:F1}%");
            sb.AppendLine();
            
            if (result.SecurityTesting.AuthenticationTestResult?.TestScenarios?.Any() == true)
            {
                sb.AppendLine("### Authentication Analysis");
                sb.AppendLine("| Scenario | Method | Expected | Actual | HTTP | Analysis | Status |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :---: | :--- | :---: |");
                foreach (var test in result.SecurityTesting.AuthenticationTestResult.TestScenarios)
                {
                    sb.AppendLine($"| {test.ScenarioName} | `{test.Method}` | {test.ExpectedBehavior} | {test.ActualBehavior} | {test.StatusCode} | {test.Analysis} | {GetStatusIcon(test.IsCompliant ? TestStatus.Passed : TestStatus.Failed)} |");
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
                    var status = AttackSimulationOutcomeResolver.Resolve(attack) switch
                    {
                        AttackSimulationOutcome.Skipped => "⏭️ SKIPPED",
                        AttackSimulationOutcome.Blocked => "🛡️ BLOCKED",
                        _ => "⚠️ REFLECTED / UNSAFE ECHO"
                    };

                    var response = string.IsNullOrEmpty(attack.ServerResponse) 
                        ? "-" 
                        : $"`{attack.ServerResponse.Replace("|", "\\|").Replace("\n", " ")}`";
                    
                    sb.AppendLine($"| {attack.AttackVector} | {attack.Description} | {status} | {response} |");
                }
                sb.AppendLine();
            }
        }

        // Detailed Findings - Protocol
        if (includeDetailedSections && result.ProtocolCompliance != null)
        {
            sb.AppendLine($"## {sectionNumber++}. Protocol Compliance");
            sb.AppendLine();

            if (result.ProtocolCompliance.CriticalErrors?.Any() == true)
            {
                sb.AppendLine("### Critical Errors");
                foreach (var error in result.ProtocolCompliance.CriticalErrors)
                {
                    sb.AppendLine($"- {error}");
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(result.ProtocolCompliance.Message))
            {
                sb.AppendLine($"**Status Detail:** {result.ProtocolCompliance.Message}");
                sb.AppendLine();
            }
            
            if (result.ProtocolCompliance.Violations?.Any() == true)
            {
                sb.AppendLine("### Compliance Violations");
                sb.AppendLine("| ID | Source | Description | Severity | Recommendation |");
                sb.AppendLine("| :--- | :--- | :--- | :---: | :--- |");
                foreach (var issue in result.ProtocolCompliance.Violations)
                {
                    var recommendation = !string.IsNullOrWhiteSpace(issue.Recommendation) ? issue.Recommendation : "-";
                    sb.AppendLine($"| {issue.CheckId} | `{ValidationRuleSourceClassifier.GetLabel(issue)}` | {issue.Description} | {issue.Severity} | {EscapeTableCell(recommendation)} |");
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
        if (includeDetailedSections && result.ToolValidation != null)
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
                AppendToolMetadataCompletenessMatrix(sb, result.ToolValidation.ToolResults);

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

                var authoritySummaries = ToolCatalogAuthoritySummaryBuilder.Build(result.ToolValidation);
                if (authoritySummaries.Count > 0)
                {
                    sb.AppendLine("### Tool Catalog Advisory Breakdown");
                    sb.AppendLine();
                    sb.AppendLine("Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or AI-oriented heuristics.");
                    sb.AppendLine();
                    sb.AppendLine("| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |");
                    sb.AppendLine("| :--- | :---: | :--- | :---: | :--- |");
                    foreach (var summary in authoritySummaries)
                    {
                        var highlights = string.Join("<br />", summary.Highlights.Select(EscapeTableCell));
                        sb.AppendLine($"| {FormatAuthorityLabel(summary.SourceLabel)} | {summary.ActiveRuleCount} | {FormatAuthorityCoverage(summary.AffectedComponents, summary.TotalComponents)} | {FormatFindingSeverity(summary.HighestSeverity)} | {highlights} |");
                    }
                    sb.AppendLine();
                }

                var guidelineFindings = ValidationFindingAggregator.SummarizeFindingsByRule(
                    result.ToolValidation.ToolResults
                        .SelectMany(tool => tool.Findings.Where(f => f.Category == "McpGuideline")),
                    ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation));

                if (guidelineFindings.Count > 0)
                {
                    sb.AppendLine("### MCP Guideline Findings");
                    sb.AppendLine();
                    sb.AppendLine("These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.");
                    sb.AppendLine("Coverage shows how prevalent each issue is across the discovered tool catalog so larger servers are judged by rate, not raw count alone.");
                    sb.AppendLine();
                    sb.AppendLine("| Rule ID | Source | Coverage | Severity | Example Components | Finding |");
                    sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :--- |");
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
                        var examples = finding.ExampleComponents.Count > 0 ? string.Join(", ", finding.ExampleComponents.Select(component => $"`{component}`")) : "-";
                        sb.AppendLine($"| `{finding.RuleId}` | `{finding.SourceLabel}` | {FormatCoverage(finding.AffectedComponents, finding.TotalComponents)} | {severityIcon} | {examples} | {finding.Summary} |");
                    }
                    sb.AppendLine();
                }
            }
        }

        // Detailed Findings - Resources
        if (includeDetailedSections && result.ResourceTesting != null)
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
        if (includeDetailedSections && result.ToolValidation != null && result.ToolValidation.AiReadinessScore >= 0)
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
                if (result.ToolValidation.AiReadinessFindings?.Any() == true)
                {
                    var aiReadinessFindings = ValidationFindingAggregator.SummarizeFindingsByRule(
                        result.ToolValidation.AiReadinessFindings,
                        ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation));

                    sb.AppendLine("| Rule ID | Source | Coverage | Severity | Finding |");
                    sb.AppendLine("| :--- | :--- | :--- | :---: | :--- |");
                    foreach (var finding in aiReadinessFindings)
                    {
                        var severity = finding.Severity switch
                        {
                            ValidationFindingSeverity.Critical => "🔴 Critical",
                            ValidationFindingSeverity.High => "🟠 High",
                            ValidationFindingSeverity.Medium => "🟡 Medium",
                            ValidationFindingSeverity.Low => "🔵 Low",
                            _ => "⚪ Info"
                        };

                        sb.AppendLine($"| `{finding.RuleId}` | `{finding.SourceLabel}` | {FormatCoverage(finding.AffectedComponents, finding.TotalComponents)} | {severity} | {finding.Summary} |");
                    }
                }
                else foreach (var issue in result.ToolValidation.AiReadinessIssues)
                {
                    sb.AppendLine($"- {issue}");
                }
                sb.AppendLine();
            }
        }

        // Protocol Capability Probes
        if (includeDetailedSections && result.ProtocolCompliance?.Message != null && result.ProtocolCompliance.Message.Contains("|"))
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
        if (includeDetailedSections && result.PerformanceTesting != null)
        {
            var hasObservedPerformanceMetrics = PerformanceMeasurementEvaluator.HasObservedMetrics(result.PerformanceTesting);
            sb.AppendLine($"## {sectionNumber++}. Performance Metrics");
            sb.AppendLine();

            if (!hasObservedPerformanceMetrics)
            {
                var unavailableReason = PerformanceMeasurementEvaluator.GetUnavailableReason(
                    result.PerformanceTesting,
                    "Performance measurements were not captured before the run ended.");

                sb.AppendLine($"**Status:** {GetStatusIcon(result.PerformanceTesting.Status)} {result.PerformanceTesting.Status}");
                sb.AppendLine("**Measurements:** unavailable");
                sb.AppendLine($"**Reason:** {unavailableReason}");

                if (result.PerformanceTesting.CriticalErrors.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("**Critical Errors:**");
                    foreach (var error in result.PerformanceTesting.CriticalErrors)
                    {
                        sb.AppendLine($"- {error}");
                    }
                }

                sb.AppendLine();
            }
            else
            {
                if (result.PerformanceTesting.Status != TestStatus.Passed)
                {
                    sb.AppendLine($"**Status:** {GetStatusIcon(result.PerformanceTesting.Status)} {result.PerformanceTesting.Status}");

                    if (!string.IsNullOrWhiteSpace(result.PerformanceTesting.Message))
                    {
                        sb.AppendLine($"**Note:** {result.PerformanceTesting.Message}");
                    }

                    sb.AppendLine();
                }

                sb.AppendLine("| Metric | Result | Verdict |");
                sb.AppendLine("| :--- | :--- | :--- |");
                sb.AppendLine($"| **Avg Latency** | {result.PerformanceTesting.LoadTesting.AverageResponseTimeMs:F2}ms | {GetPerformanceVerdict(result.PerformanceTesting.LoadTesting.AverageResponseTimeMs)} |");
                sb.AppendLine($"| **P95 Latency** | {result.PerformanceTesting.LoadTesting.P95ResponseTimeMs:F2}ms | - |");
                sb.AppendLine($"| **Throughput** | {result.PerformanceTesting.LoadTesting.RequestsPerSecond:F2} req/sec | - |");
                sb.AppendLine($"| **Error Rate** | {result.PerformanceTesting.LoadTesting.ErrorRate:F2}% | {(result.PerformanceTesting.LoadTesting.ErrorRate > 0 ? "⚠️ Check Logs" : "✅ Clean")} |");
                sb.AppendLine($"| **Requests** | {result.PerformanceTesting.LoadTesting.SuccessfulRequests}/{result.PerformanceTesting.LoadTesting.TotalRequests} successful | - |");
                if (result.PerformanceTesting.LoadTesting.ProbeRoundsExecuted > 1)
                {
                    sb.AppendLine($"| **Probe Rounds** | {result.PerformanceTesting.LoadTesting.ProbeRoundsExecuted} | ℹ️ Calibrated |");
                }
                if (result.PerformanceTesting.LoadTesting.ObservedRateLimitedRequests > 0)
                {
                    sb.AppendLine($"| **Observed Rate Limits** | {result.PerformanceTesting.LoadTesting.ObservedRateLimitedRequests} request(s) | ⚠️ Throttling observed |");
                }
                if (result.PerformanceTesting.LoadTesting.ObservedTransientFailures > 0)
                {
                    sb.AppendLine($"| **Observed Transient Failures** | {result.PerformanceTesting.LoadTesting.ObservedTransientFailures} request(s) | ⚠️ Retry pressure observed |");
                }
                sb.AppendLine();

                // Score breakdown — show why the performance score is what it is
                if (result.PerformanceTesting.Score < 100)
                {
                    sb.AppendLine("#### Score Breakdown");
                    sb.AppendLine();
                    sb.AppendLine("| Factor | Threshold | Observed | Penalty |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");

                    var nonRateLimitedFailures = result.PerformanceTesting.LoadTesting.NonRateLimitedFailedRequests;
                    if (nonRateLimitedFailures > 0)
                    {
                        var errorPenalty = nonRateLimitedFailures * 5;
                        sb.AppendLine($"| Server failures | 0 non-rate-limited failures | {nonRateLimitedFailures} non-rate-limited failures | −{errorPenalty} points |");
                    }

                    var rateLimitedRequests = result.PerformanceTesting.LoadTesting.RateLimitedRequests;
                    if (rateLimitedRequests > 0)
                    {
                        sb.AppendLine($"| Rate limiting | surfaced separately | {rateLimitedRequests} rate-limited requests | −0 points |");
                    }

                    var avgLatency = result.PerformanceTesting.LoadTesting.AverageResponseTimeMs;
                    if (avgLatency > 200)
                    {
                        var latencyPenalty = (avgLatency - 200) / 20.0;
                        sb.AppendLine($"| Avg latency | ≤ 200ms | {avgLatency:F1}ms | −{latencyPenalty:F1} points |");
                    }

                    sb.AppendLine($"| **Final score** | 100 | | **{result.PerformanceTesting.Score:F1}** |");
                    sb.AppendLine();
                }

                if (result.PerformanceTesting.PerformanceBottlenecks?.Count > 0)
                {
                    sb.AppendLine("#### Identified Bottlenecks");
                    sb.AppendLine();
                    foreach (var bottleneck in result.PerformanceTesting.PerformanceBottlenecks)
                    {
                        sb.AppendLine($"- {bottleneck}");
                    }
                    sb.AppendLine();
                }
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
        var producer = ResolveProducer(result);
        sb.AppendLine("---");
        sb.AppendLine($"*Produced with [{producer.Name}]({producer.RepositoryUrl}) · [{producer.PackageId} on NuGet]({producer.PackageUrl})*");

        return sb.ToString();
    }

    private void AppendPriorityFindingsSection(StringBuilder sb, ValidationResult result, ref int sectionNumber)
    {
        var keyFindings = CollectPriorityFindings(result);
        if (keyFindings.Count == 0)
        {
            return;
        }

        sb.AppendLine($"## {sectionNumber++}. Priority Findings");
        sb.AppendLine();
        sb.AppendLine("These are the highest-signal outcomes from this validation run.");
        sb.AppendLine();
        foreach (var finding in keyFindings)
        {
            sb.AppendLine($"- {finding}");
        }
        sb.AppendLine();
    }

    private void AppendActionHintsSection(StringBuilder sb, IReadOnlyList<string> actionHints, ref int sectionNumber)
    {
        if (actionHints.Count == 0)
        {
            return;
        }

        sb.AppendLine($"## {sectionNumber++}. Action Hints");
        sb.AppendLine();
        sb.AppendLine("Compact next-step guidance derived from the highest-signal evidence in this run.");
        sb.AppendLine();
        foreach (var hint in actionHints)
        {
            sb.AppendLine($"- {hint}");
        }
        sb.AppendLine();
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

    private static ValidationProducerInfo ResolveProducer(ValidationResult result)
    {
        var defaults = ValidationProducerInfo.CreateDefault();
        var producer = result.Producer ?? defaults;

        producer.Name = string.IsNullOrWhiteSpace(producer.Name) ? defaults.Name : producer.Name;
        producer.PackageId = string.IsNullOrWhiteSpace(producer.PackageId) ? defaults.PackageId : producer.PackageId;
        producer.RepositoryUrl = string.IsNullOrWhiteSpace(producer.RepositoryUrl) ? defaults.RepositoryUrl : producer.RepositoryUrl;
        producer.PackageUrl = string.IsNullOrWhiteSpace(producer.PackageUrl) ? defaults.PackageUrl : producer.PackageUrl;
        return producer;
    }

    private void AppendBootstrapSection(StringBuilder sb, ValidationResult result, HealthCheckResult? bootstrapHealth, ref int sectionNumber)
    {
        if (bootstrapHealth == null)
        {
            return;
        }

        sb.AppendLine($"## {sectionNumber++}. Connectivity & Session Bootstrap");
        sb.AppendLine();
        sb.AppendLine("This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.");
        sb.AppendLine();
        sb.AppendLine("| Bootstrap Signal | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Bootstrap State** | {FormatHealthDispositionLabel(bootstrapHealth)} |");
        sb.AppendLine($"| **Validation Proceeded Under Deferment** | {FormatDeferredValidationLabel(bootstrapHealth)} |");
        sb.AppendLine($"| **Initialize Handshake** | {FormatHandshakeOutcomeLabel(bootstrapHealth)} |");

        if (bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode)
        {
            sb.AppendLine($"| **Handshake HTTP Status** | `HTTP {statusCode}` |");
        }

        if (bootstrapHealth.ResponseTimeMs > 0)
        {
            sb.AppendLine($"| **Handshake Duration** | {bootstrapHealth.ResponseTimeMs:F1} ms |");
        }

        var effectiveProtocolVersion = result.ProtocolVersion ?? bootstrapHealth.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(effectiveProtocolVersion) && !string.Equals(effectiveProtocolVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| **Negotiated Protocol** | `{effectiveProtocolVersion}` |");
        }

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion) && !string.Equals(bootstrapHealth.ServerVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| **Observed Server Version** | `{bootstrapHealth.ServerVersion}` |");
        }

        sb.AppendLine($"| **Server Profile Resolution** | `{FormatProfileLabel(result)}` |");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ErrorMessage))
        {
            sb.AppendLine($"> **Bootstrap Note:** {bootstrapHealth.ErrorMessage}");
            sb.AppendLine();
        }

        sb.AppendLine(GetBootstrapNarrative(bootstrapHealth));
        sb.AppendLine();
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

    private void AppendClientCompatibilitySection(StringBuilder sb, ValidationResult result, ref int sectionNumber)
    {
        if (result.ClientCompatibility?.Assessments.Count > 0 != true)
        {
            return;
        }

        sb.AppendLine($"## {sectionNumber++}. Client Profile Compatibility");
        sb.AppendLine();
        sb.AppendLine("Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.");
        sb.AppendLine();
        sb.AppendLine("| Client Profile | Status | Requirements | Documentation |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var assessment in result.ClientCompatibility.Assessments)
        {
            var requirementSummary = $"{assessment.PassedRequirements} passed / {assessment.WarningRequirements} warnings / {assessment.FailedRequirements} failed";
            var documentation = string.IsNullOrWhiteSpace(assessment.DocumentationUrl)
                ? "-"
                : $"<{assessment.DocumentationUrl}>";
            sb.AppendLine($"| **{assessment.DisplayName}** | {FormatClientCompatibilityStatus(assessment.Status)} | {requirementSummary} | {documentation} |");
        }

        sb.AppendLine();

        foreach (var assessment in result.ClientCompatibility.Assessments)
        {
            sb.AppendLine($"### {assessment.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"**Status:** {FormatClientCompatibilityStatus(assessment.Status)}");
            sb.AppendLine();
            sb.AppendLine($"{assessment.Summary}");
            sb.AppendLine();

            var noteworthyRequirements = assessment.Requirements
                .Where(requirement => requirement.Outcome is ClientProfileRequirementOutcome.Warning or ClientProfileRequirementOutcome.Failed)
                .ToList();

            if (noteworthyRequirements.Count == 0)
            {
                sb.AppendLine("- No client-specific compatibility gaps were detected.");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
            foreach (var requirement in noteworthyRequirements)
            {
                var ruleIds = requirement.RuleIds.Count > 0
                    ? string.Join(", ", requirement.RuleIds.Select(id => $"`{id}`"))
                    : "-";
                var components = requirement.ExampleComponents.Count > 0
                    ? string.Join(", ", requirement.ExampleComponents.Select(c => $"`{c}`"))
                    : "-";

                sb.AppendLine($"| {EscapeTableCell(requirement.Title)} | {requirement.Level} | {FormatRequirementOutcome(requirement.Outcome)} | {ruleIds} | {components} | {EscapeTableCell(requirement.Summary)} |");
            }

            sb.AppendLine();

            // Remediation guidance for failed/warning requirements
            var requirementsWithRemediation = noteworthyRequirements
                .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Recommendation))
                .ToList();

            if (requirementsWithRemediation.Count > 0)
            {
                sb.AppendLine("#### Remediation");
                sb.AppendLine();
                foreach (var requirement in requirementsWithRemediation)
                {
                    var outcomeIcon = requirement.Outcome == ClientProfileRequirementOutcome.Failed ? "❌" : "⚠️";
                    sb.AppendLine($"- {outcomeIcon} **{EscapeTableCell(requirement.Title)}:** {EscapeTableCell(requirement.Recommendation!)}");
                }
                sb.AppendLine();
            }
        }
    }

    private void AddMatrixRow(StringBuilder sb, string category, TestStatus? status, double? score, int? issues, string? scoreOverride = null)
    {
        if (status == null) return;
        
        var statusStr = status == TestStatus.Skipped ? "Skipped" : status.ToString();
        var scoreStr = status == TestStatus.Skipped ? "-" : scoreOverride ?? $"{score:F1}%";
        var issuesStr = issues.HasValue && issues > 0 ? $"**{issues}**" : "-";
        var icon = GetStatusIcon(status.Value);

        sb.AppendLine($"| {category} | {icon} {statusStr} | {scoreStr} | {issuesStr} |");
    }

    private static string? FormatPerformanceMatrixScore(PerformanceTestResult? performance)
    {
        if (performance == null || performance.Status == TestStatus.Skipped)
        {
            return null;
        }

        return PerformanceMeasurementEvaluator.HasObservedMetrics(performance)
            ? null
            : "Unavailable";
    }

    private static int GetProtocolIssueCount(ComplianceTestResult? result)
    {
        if (result == null)
        {
            return 0;
        }

        return (result.Violations?.Count ?? 0) + (result.CriticalErrors?.Count ?? 0);
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

    private static string FormatCoverage(int affectedComponents, int totalComponents)
    {
        if (affectedComponents <= 0)
        {
            return "0";
        }

        if (totalComponents <= 0)
        {
            return affectedComponents.ToString();
        }

        var percentage = ValidationFindingAggregator.CalculateCoverageRatio(affectedComponents, totalComponents) * 100;
        return $"{affectedComponents}/{totalComponents} ({percentage.ToString("0", CultureInfo.InvariantCulture)}%)";
    }

    private static string FormatAuthorityCoverage(int affectedComponents, int totalComponents)
    {
        if (totalComponents > 0)
        {
            var percentage = ValidationFindingAggregator.CalculateCoverageRatio(affectedComponents, totalComponents) * 100;
            return $"{affectedComponents}/{totalComponents} ({percentage.ToString("0", CultureInfo.InvariantCulture)}%)";
        }

        return affectedComponents.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatAuthorityLabel(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return "Unspecified";
        }

        return char.ToUpperInvariant(authority[0]) + authority[1..].ToLowerInvariant();
    }

    private static string FormatFindingSeverity(ValidationFindingSeverity? severity)
    {
        if (severity == null)
        {
            return "-";
        }

        return severity.Value switch
        {
            ValidationFindingSeverity.Critical => "🔴 Critical",
            ValidationFindingSeverity.High => "🟠 High",
            ValidationFindingSeverity.Medium => "🟡 Medium",
            ValidationFindingSeverity.Low => "🔵 Low",
            _ => "⚪ Info"
        };
    }

    private static string FormatClientCompatibilityStatus(ClientProfileCompatibilityStatus status) => status switch
    {
        ClientProfileCompatibilityStatus.Compatible => "✅ Compatible",
        ClientProfileCompatibilityStatus.CompatibleWithWarnings => "⚠️ Compatible with warnings",
        ClientProfileCompatibilityStatus.Incompatible => "❌ Incompatible",
        _ => "ℹ️ Unknown"
    };

    private static string FormatRequirementOutcome(ClientProfileRequirementOutcome outcome) => outcome switch
    {
        ClientProfileRequirementOutcome.Satisfied => "✅ Satisfied",
        ClientProfileRequirementOutcome.Warning => "⚠️ Warning",
        ClientProfileRequirementOutcome.Failed => "❌ Failed",
        ClientProfileRequirementOutcome.NotApplicable => "➖ Not applicable",
        _ => "ℹ️ Unknown"
    };

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
    }

    private static bool ShouldIncludeDetailedSections(ValidationResult result)
    {
        return result.ValidationConfig.Reporting.IncludesDetailedSections();
    }

    private static void AppendToolMetadataCompletenessMatrix(StringBuilder sb, List<IndividualToolResult> toolResults)
    {
        sb.AppendLine("### Tool Metadata Completeness");
        sb.AppendLine();
        sb.AppendLine("Annotation coverage across the discovered tool catalog. Missing annotations reduce AI agent safety and UX quality.");
        sb.AppendLine();
        sb.AppendLine("| Tool | title | description | readOnlyHint | destructiveHint | openWorldHint | idempotentHint |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var tool in toolResults)
        {
            var title = !string.IsNullOrWhiteSpace(tool.DisplayTitle) ? "✅" : "❌";
            var description = !string.IsNullOrWhiteSpace(tool.Description) ? "✅" : "❌";
            var readOnly = tool.ReadOnlyHint.HasValue ? "✅" : "❌";
            var destructive = tool.DestructiveHint.HasValue ? "✅" : "❌";
            var openWorld = tool.OpenWorldHint.HasValue ? "✅" : "❌";
            var idempotent = tool.IdempotentHint.HasValue ? "✅" : "❌";
            sb.AppendLine($"| `{tool.ToolName}` | {title} | {description} | {readOnly} | {destructive} | {openWorld} | {idempotent} |");
        }

        // Summary row
        var total = toolResults.Count;
        if (total > 0)
        {
            var titleCount = toolResults.Count(t => !string.IsNullOrWhiteSpace(t.DisplayTitle));
            var descCount = toolResults.Count(t => !string.IsNullOrWhiteSpace(t.Description));
            var roCount = toolResults.Count(t => t.ReadOnlyHint.HasValue);
            var destCount = toolResults.Count(t => t.DestructiveHint.HasValue);
            var owCount = toolResults.Count(t => t.OpenWorldHint.HasValue);
            var idempCount = toolResults.Count(t => t.IdempotentHint.HasValue);
            sb.AppendLine($"| **Coverage** | **{titleCount}/{total}** | **{descCount}/{total}** | **{roCount}/{total}** | **{destCount}/{total}** | **{owCount}/{total}** | **{idempCount}/{total}** |");
        }

        sb.AppendLine();
    }

    private static IReadOnlyList<string> CollectPriorityFindings(ValidationResult result)
    {
        var findings = new List<string>();

        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            findings.Add($"Policy {policyOutcome.Mode} blocked the run: {policyOutcome.Summary}");
            findings.AddRange(policyOutcome.Reasons.Take(2));
        }

        if (result.ClientCompatibility?.Assessments.Count > 0)
        {
            findings.AddRange(result.ClientCompatibility.Assessments
                .Where(assessment => assessment.Status == ClientProfileCompatibilityStatus.Incompatible)
                .Take(2)
                .Select(assessment => $"Client profile {assessment.DisplayName}: {assessment.StatusLabel}. {assessment.Summary}"));
        }

        if (result.CriticalErrors.Count > 0)
        {
            findings.AddRange(result.CriticalErrors.Take(2));
        }

        if (result.SecurityTesting?.Vulnerabilities.Count > 0)
        {
            findings.AddRange(result.SecurityTesting.Vulnerabilities
                .OrderByDescending(vulnerability => vulnerability.Severity)
                .Take(2)
                .Select(vulnerability => $"{vulnerability.Id}: {vulnerability.Description}"));
        }

        if (result.ProtocolCompliance?.Violations.Count > 0)
        {
            findings.AddRange(result.ProtocolCompliance.Violations
                .OrderByDescending(violation => violation.Severity)
                .Take(2)
                .Select(violation => $"{violation.CheckId}: {violation.Description}"));
        }

        return findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();
    }

    private static HealthCheckResult? ResolveBootstrapHealth(ValidationResult result)
    {
        if (result.BootstrapHealth != null)
        {
            return result.BootstrapHealth;
        }

        if (result.InitializationHandshake == null)
        {
            return null;
        }

        return new HealthCheckResult
        {
            IsHealthy = result.InitializationHandshake.IsSuccessful,
            Disposition = ValidationReliability.ClassifyHealthCheck(result.InitializationHandshake),
            ResponseTimeMs = result.InitializationHandshake.Transport.Duration.TotalMilliseconds,
            ServerVersion = result.InitializationHandshake.Payload?.ServerInfo?.Version,
            ProtocolVersion = result.InitializationHandshake.Payload?.ProtocolVersion,
            ErrorMessage = result.InitializationHandshake.IsSuccessful ? null : result.InitializationHandshake.Error,
            InitializationDetails = result.InitializationHandshake
        };
    }

    private static string FormatHealthDispositionLabel(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "✅ Healthy",
        HealthCheckDisposition.Protected => "🔒 Reachable (Protected)",
        HealthCheckDisposition.TransientFailure => "⚠️ Transient Failure",
        HealthCheckDisposition.Inconclusive => "ℹ️ Inconclusive",
        HealthCheckDisposition.Unhealthy => "❌ Unhealthy",
        _ => bootstrapHealth.IsHealthy ? "✅ Healthy" : "ℹ️ Unknown"
    };

    private static string FormatDeferredValidationLabel(HealthCheckResult bootstrapHealth)
    {
        if (bootstrapHealth.AllowsDeferredValidation && !bootstrapHealth.IsHealthy)
        {
            return "Yes — validation continued with a calibrated advisory bootstrap state.";
        }

        return "No — validation started from a clean bootstrap state.";
    }

    private static string FormatHandshakeOutcomeLabel(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "✅ Initialize handshake succeeded.",
        HealthCheckDisposition.Protected => "🔒 Initialize handshake confirmed a protected endpoint boundary.",
        HealthCheckDisposition.TransientFailure => "⚠️ Initialize handshake encountered a transient capacity or transport constraint.",
        HealthCheckDisposition.Inconclusive => "ℹ️ Initialize handshake responded, but could not fully establish readiness.",
        HealthCheckDisposition.Unhealthy => "❌ Initialize handshake failed definitively.",
        _ => bootstrapHealth.IsHealthy ? "✅ Initialize handshake succeeded." : "ℹ️ Initialize handshake outcome is unknown."
    };

    private static string GetBootstrapNarrative(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.",
        HealthCheckDisposition.Protected => "Validation continued because the endpoint was classified as protected rather than unreachable; later authenticated checks provide the authoritative evidence.",
        HealthCheckDisposition.TransientFailure => "Validation continued because the preflight issue matched a retry-worthy transient constraint rather than a hard endpoint failure.",
        HealthCheckDisposition.Inconclusive => "Validation continued with caution because the bootstrap handshake was inconclusive but not definitively unhealthy.",
        HealthCheckDisposition.Unhealthy => "Bootstrap did not establish a viable validation path; any subsequent output should be treated as partial evidence only.",
        _ => "Bootstrap disposition was not available."
    };
}
