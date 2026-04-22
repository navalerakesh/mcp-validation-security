using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Scoring;

/// <summary>
/// Computes the MCP Trust Assessment from a completed ValidationResult.
/// 
/// Scoring hierarchy (RFC 2119 aligned):
/// - MUST checks: Hard compliance gates. Any failure caps trust at L2 max.
/// - SHOULD checks: Weighted penalties. Reduce dimension scores.
/// - MAY checks: Informational only. Zero scoring impact.
///
/// Trust is derived from a weighted multi-dimensional score with explicit caps:
/// - Blocking security failures force L1.
/// - Protocol MUST failures cap at L2.
/// - Otherwise, the weighted score determines the level.
/// </summary>
public static class McpTrustCalculator
{
    private static readonly HashSet<string> SentenceLeadingPromptInjectionPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "you are",
        "act as",
        "pretend"
    };

    public static McpTrustAssessment Calculate(ValidationResult result)
    {
        var assessment = new McpTrustAssessment();

        // ─── Run Tiered Compliance Checks ────────────────────────────
        RunMustChecks(result, assessment);
        RunShouldChecks(result, assessment);
        RunMayChecks(result, assessment);

        // ─── Dimension 1: Protocol Compliance ────────────────────────
        assessment.ProtocolCompliance = result.ProtocolCompliance?.ComplianceScore ?? 0;

        // ─── Dimension 2: Security Posture ───────────────────────────
        assessment.SecurityPosture = result.SecurityTesting?.SecurityScore ?? 0;

        // ─── Dimension 3: AI Safety ─────────────────────────────────
        assessment.AiSafety = CalculateAiSafety(result, assessment);

        // ─── Dimension 4: Operational Readiness ──────────────────────
        assessment.OperationalReadiness = ValidationCalibration.GetOperationalReadinessScore(result.ServerConfig, result.PerformanceTesting);

        // ─── Determine Trust Level ───────────────────────────────────
        if (ValidationCalibration.HasBlockingSecurityFailure(result) || result.CriticalErrors.Count > 0)
        {
            assessment.TrustLevel = McpTrustLevel.L1_Untrusted;
            return assessment;
        }

        var weightedTrustScore =
            assessment.ProtocolCompliance * ScoringConstants.TrustWeightProtocol +
            assessment.SecurityPosture * ScoringConstants.TrustWeightSecurity +
            assessment.AiSafety * ScoringConstants.TrustWeightAiSafety +
            assessment.OperationalReadiness * ScoringConstants.TrustWeightOperations;

        var calculatedLevel = weightedTrustScore switch
        {
            >= ScoringConstants.TrustL5Threshold => McpTrustLevel.L5_CertifiedSecure,
            >= ScoringConstants.TrustL4Threshold => McpTrustLevel.L4_Trusted,
            >= ScoringConstants.TrustL3Threshold => McpTrustLevel.L3_Acceptable,
            >= ScoringConstants.TrustL2Threshold => McpTrustLevel.L2_Caution,
            _ => McpTrustLevel.L1_Untrusted
        };

        var protocolSecurityCap = GetProtocolSecurityCap(assessment);
        if (protocolSecurityCap.HasValue && calculatedLevel > protocolSecurityCap.Value)
        {
            calculatedLevel = protocolSecurityCap.Value;
        }

        if (assessment.MustFailCount > 0)
        {
            assessment.TrustLevel = calculatedLevel > McpTrustLevel.L2_Caution
                ? McpTrustLevel.L2_Caution
                : calculatedLevel;
            return assessment;
        }

        assessment.TrustLevel = calculatedLevel;

        return assessment;
    }

    // ─── MUST Checks: Hard Compliance Gates ──────────────────────────

    private static void RunMustChecks(ValidationResult result, McpTrustAssessment assessment)
    {
        // Protocol MUST checks
        AddMustCheck(assessment, McpComplianceTiers.Must.InitializeResponse, "initialize",
            !HasViolation(result.ProtocolCompliance?.Violations, ValidationConstants.CheckIds.ProtocolInitializeResponse),
            HasViolation(result.ProtocolCompliance?.Violations, ValidationConstants.CheckIds.ProtocolInitializeResponse) ? "Initialize failed" : null);

        // Only check response structure if we got a successful response
        if (result.ProtocolCompliance != null && result.ProtocolCompliance.Status != TestStatus.Skipped)
        {
            AddMustCheck(assessment, McpComplianceTiers.Must.CapabilitiesInResponse, "initialize",
                !HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingCapabilities),
                HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingCapabilities) ? "capabilities object missing from initialize" : null);

            AddMustCheck(assessment, McpComplianceTiers.Must.ProtocolVersionInResponse, "initialize",
                !HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingProtocolVersion),
                HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingProtocolVersion) ? "protocolVersion missing from initialize" : null);

            AddMustCheck(assessment, McpComplianceTiers.Must.ServerInfoPresent, "initialize",
                !HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfo),
                HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfo) ? "serverInfo missing from initialize" : null);

            AddMustCheck(assessment, McpComplianceTiers.Must.ServerInfoHasName, "initialize",
                !HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoName),
                HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoName) ? "serverInfo.name missing from initialize" : null);

            AddMustCheck(assessment, McpComplianceTiers.Must.ServerInfoHasVersion, "initialize",
                !HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoVersion),
                HasViolation(result.ProtocolCompliance.Violations, ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoVersion) ? "serverInfo.version missing from initialize" : null);
        }

        // Tool MUST checks
        if (result.ToolValidation != null && result.ToolValidation.Status != TestStatus.Skipped)
        {
            AddMustCheck(assessment, McpComplianceTiers.Must.ToolsListReturnsArray, "tools/list",
                result.ToolValidation.Status is not (TestStatus.Error or TestStatus.Failed),
                result.ToolValidation.Status is TestStatus.Error or TestStatus.Failed ? "tools/list returned an invalid response" : null);

            // Check if any tool result flagged missing content[] array
            var hasMissingContent = result.ToolValidation.ToolResults?.Any(t =>
                HasFinding(t.Findings, ValidationFindingRuleIds.ToolCallMissingContentArray)) == true;
            if (result.ToolValidation.ToolResults?.Any(t =>
                t.ExecutionSuccessful || HasFinding(t.Findings, ValidationFindingRuleIds.ToolCallMissingContentArray)) == true)
            {
                AddMustCheck(assessment, McpComplianceTiers.Must.ToolCallReturnsContent, "tools/call",
                    !hasMissingContent, hasMissingContent ? "tools/call result missing content[] array" : null);
            }
        }

        // Resource MUST checks
        if (result.ResourceTesting != null && result.ResourceTesting.Status != TestStatus.Skipped)
        {
            var missingUri = result.ResourceTesting.ResourceResults?.Any(r =>
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceMissingUri)) == true;

            if (result.ResourceTesting.ResourceResults?.Count > 0)
            {
                AddMustCheck(assessment, McpComplianceTiers.Must.ResourceHasUri, "resources/list",
                    !missingUri, missingUri ? "Resource missing 'uri' field" : null);
            }

            var missingContentUri = result.ResourceTesting.ResourceResults?.Any(r =>
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceReadMissingContentUri)) == true;
            var missingTextBlob = result.ResourceTesting.ResourceResults?.Any(r =>
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceReadMissingTextOrBlob)) == true;

            if (result.ResourceTesting.ResourceResults?.Any(r =>
                r.AccessSuccessful ||
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceReadMissingContentUri) ||
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceReadMissingTextOrBlob) ||
                HasFinding(r.Findings, ValidationFindingRuleIds.ResourceReadMissingContentArray)) == true)
            {
                AddMustCheck(assessment, McpComplianceTiers.Must.ResourceContentHasUri, "resources/read",
                    !missingContentUri, missingContentUri ? "contents[] missing 'uri'" : null);

                AddMustCheck(assessment, McpComplianceTiers.Must.ResourceContentHasTextOrBlob, "resources/read",
                    !missingTextBlob, missingTextBlob ? "contents[] missing text/blob" : null);
            }
        }

        // Prompt MUST checks
        if (result.PromptTesting != null && result.PromptTesting.Status != TestStatus.Skipped)
        {
            var missingMessages = result.PromptTesting.PromptResults?.Any(p =>
                HasFinding(p.Findings, ValidationFindingRuleIds.PromptGetMissingMessagesArray)) == true;
            var missingRole = result.PromptTesting.PromptResults?.Any(p =>
                HasFinding(p.Findings, ValidationFindingRuleIds.PromptMessageMissingRole)) == true;

            if (result.PromptTesting.PromptResults?.Any(p =>
                p.ExecutionSuccessful ||
                HasFinding(p.Findings, ValidationFindingRuleIds.PromptGetMissingMessagesArray) ||
                HasFinding(p.Findings, ValidationFindingRuleIds.PromptMessageMissingRole)) == true)
            {
                AddMustCheck(assessment, McpComplianceTiers.Must.PromptsGetReturnsMessages, "prompts/get",
                    !missingMessages, missingMessages ? "prompts/get missing messages[] array" : null);

                AddMustCheck(assessment, McpComplianceTiers.Must.MessageHasRole, "prompts/get",
                    !missingRole, missingRole ? "Message missing 'role' field" : null);
            }
        }

        // Security MUST checks
        if (result.SecurityTesting != null)
        {
            AddMustCheck(assessment, McpComplianceTiers.Must.StandardErrorCodes, "errors",
                result.ProtocolCompliance?.JsonRpcCompliance?.ErrorHandlingCompliant != false,
                result.ProtocolCompliance?.JsonRpcCompliance?.ErrorHandlingCompliant == false ? "Non-standard error codes" : null);
        }
    }

    // ─── SHOULD Checks: Weighted Penalties ───────────────────────────

    private static void RunShouldChecks(ValidationResult result, McpTrustAssessment assessment)
    {
        // Tool descriptions
        if (result.ToolValidation?.AiReadinessFindings != null)
        {
            var hasUndescribed = result.ToolValidation.AiReadinessFindings.Any(f => f.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
            AddShouldCheck(assessment, McpComplianceTiers.Should.ToolHasDescription, "tools/list", !hasUndescribed);

            var hasVagueTypes = result.ToolValidation.AiReadinessFindings.Any(f => f.RuleId == ValidationFindingRuleIds.AiReadinessVagueStringSchema);
            AddShouldCheck(assessment, McpComplianceTiers.Should.DescriptiveParameterTypes, "tools/list", !hasVagueTypes);
        }

        // isError field
        if (result.ToolValidation?.ToolResults != null)
        {
            var missingIsError = result.ToolValidation.ToolResults.Any(t =>
                HasFinding(t.Findings, ValidationFindingRuleIds.ToolCallMissingIsError));
            AddShouldCheck(assessment, McpComplianceTiers.Should.IsErrorFieldPresent, "tools/call", !missingIsError);
        }

        // WWW-Authenticate
        if (result.SecurityTesting?.AuthenticationTestResult?.TestScenarios != null)
        {
            var hasWwwAuth = result.ToolValidation?.AuthenticationSecurity?.HasProperAuthHeaders == true;
            // Only check if server requires auth
            if (result.ToolValidation?.AuthenticationSecurity?.AuthenticationRequired == true)
            {
                AddShouldCheck(assessment, McpComplianceTiers.Should.WwwAuthenticateHeader, "auth", hasWwwAuth);
            }
        }

        // Sanitize outputs (injection reflection = failure)
        if (result.SecurityTesting?.AttackSimulations != null)
        {
            var reflected = result.SecurityTesting.AttackSimulations.Any(a => a.AttackSuccessful);
            AddShouldCheck(assessment, McpComplianceTiers.Should.SanitizeToolOutputs, "security", !reflected);
        }

        // Token efficiency
        if (result.ToolValidation != null && result.ToolValidation.EstimatedTokenCount > 0)
        {
            AddShouldCheck(assessment, McpComplianceTiers.Should.TokenEfficiency, "tools/list",
                result.ToolValidation.EstimatedTokenCount <= ScoringConstants.TokenPenaltyThreshold);
        }
    }

    // ─── MAY Checks: Informational Only ──────────────────────────────

    private static void RunMayChecks(ValidationResult result, McpTrustAssessment assessment)
    {
        AddMayCheck(assessment, McpComplianceTiers.May.Logging, "capabilities",
            HasFinding(result.ProtocolCompliance?.Findings, ValidationFindingRuleIds.OptionalCapabilityLoggingSupported));

        AddMayCheck(assessment, McpComplianceTiers.May.Sampling, "capabilities",
            HasFinding(result.ProtocolCompliance?.Findings, ValidationFindingRuleIds.OptionalCapabilitySamplingSupported));

        AddMayCheck(assessment, McpComplianceTiers.May.Roots, "capabilities",
            HasFinding(result.ProtocolCompliance?.Findings, ValidationFindingRuleIds.OptionalCapabilityRootsSupported));

        AddMayCheck(assessment, McpComplianceTiers.May.Completion, "capabilities",
            HasFinding(result.ProtocolCompliance?.Findings, ValidationFindingRuleIds.OptionalCapabilityCompletionsSupported));

        // Resource templates (checked in resource issues)
        if (result.ResourceTesting != null)
        {
            var hasTemplates = result.ResourceTesting.Issues?.Any(i => i.Contains("Resource templates:") && i.Contains("discovered")) == true;
            AddMayCheck(assessment, McpComplianceTiers.May.ResourceTemplates, "resources", hasTemplates);
        }

        var hasToolAnnotations = result.ToolValidation?.ToolResults?.Any(t =>
            !string.IsNullOrWhiteSpace(t.DisplayTitle) ||
            t.ReadOnlyHint.HasValue ||
            t.DestructiveHint.HasValue ||
            t.OpenWorldHint.HasValue ||
            t.IdempotentHint.HasValue) == true;

        AddMayCheck(assessment, McpComplianceTiers.May.ToolAnnotations, "tools",
            hasToolAnnotations);
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static void AddMustCheck(McpTrustAssessment a, string req, string component, bool passed, string? detail = null)
    {
        a.MustTotalCount++;
        if (passed) a.MustPassCount++;
        else a.MustFailCount++;

        a.TierChecks.Add(new ComplianceTierCheck
        {
            Tier = "MUST", Requirement = req, Component = component, Passed = passed, Detail = detail
        });
    }

    private static void AddShouldCheck(McpTrustAssessment a, string req, string component, bool passed, string? detail = null)
    {
        a.ShouldTotalCount++;
        if (passed) a.ShouldPassCount++;
        else a.ShouldFailCount++;

        a.TierChecks.Add(new ComplianceTierCheck
        {
            Tier = "SHOULD", Requirement = req, Component = component, Passed = passed, Detail = detail
        });
    }

    private static void AddMayCheck(McpTrustAssessment a, string req, string component, bool supported, string? detail = null)
    {
        a.MayTotal++;
        if (supported) a.MaySupported++;

        a.TierChecks.Add(new ComplianceTierCheck
        {
            Tier = "MAY", Requirement = req, Component = component, Passed = supported, Detail = detail
        });
    }

    private static double CalculateAiSafety(ValidationResult result, McpTrustAssessment assessment)
    {
        double score = 100.0;
        var toolCatalogSize = ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation);

        // Start from AI Readiness score if available
        if (result.ToolValidation?.AiReadinessScore >= 0)
        {
            score = result.ToolValidation.AiReadinessScore;
        }

        // ─── Boundary Check: Destructive Tools ──────────────────────
        if (result.ToolValidation?.ToolResults != null)
        {
            foreach (var tool in result.ToolValidation.ToolResults)
            {
                var name = tool.ToolName?.ToLowerInvariant() ?? "";
                var isLikelyDestructive = tool.DestructiveHint == true ||
                                          (tool.ReadOnlyHint != true &&
                                           (name.Contains("delete") || name.Contains("remove") ||
                                            name.Contains("drop") || name.Contains("destroy") ||
                                            name.Contains("write") || name.Contains("update") ||
                                            name.Contains("create") || name.Contains("execute") ||
                                            name.Contains("run") || name.Contains("send")));

                if (isLikelyDestructive)
                {
                    assessment.DestructiveToolCount++;
                    var severity = tool.DestructiveHint == true ? "High" : "Medium";
                    var description = tool.DestructiveHint == true
                        ? $"Tool '{tool.ToolName}' declares destructiveHint=true. AI agents SHOULD require human confirmation before invocation."
                        : $"Tool '{tool.ToolName}' appears to perform write/destructive operations. AI agents SHOULD require human confirmation.";

                    assessment.BoundaryFindings.Add(new AiBoundaryFinding
                    {
                        Category = "Destructive",
                        Component = tool.ToolName ?? "unknown",
                        Severity = severity,
                        Description = description,
                        Mitigation = "Add annotations.readOnlyHint=false and annotations.destructiveHint=true to tool definition."
                    });
                }
            }
        }

        // ─── Boundary Check: Data Exfiltration Risk ─────────────────
        if (result.ToolValidation?.ToolResults != null)
        {
            foreach (var tool in result.ToolValidation.ToolResults)
            {
                var name = tool.ToolName?.ToLowerInvariant() ?? "";
                var parameterNames = tool.InputParameterNames.Select(p => p.ToLowerInvariant()).ToList();
                var descriptionText = string.Join(' ', new[] { tool.DisplayTitle, tool.Description }.Where(text => !string.IsNullOrWhiteSpace(text))).ToLowerInvariant();
                foreach (var pattern in ScoringConstants.ExfiltrationRiskPatterns)
                {
                    var hasParameterEvidence = parameterNames.Any(p => p.Contains(pattern));
                    var hasBehaviorEvidence = name.Contains(pattern) || descriptionText.Contains(pattern) || tool.OpenWorldHint == true;

                    if (hasParameterEvidence && hasBehaviorEvidence)
                    {
                        assessment.DataExfiltrationRiskCount++;
                        assessment.BoundaryFindings.Add(new AiBoundaryFinding
                        {
                            Category = "Exfiltration",
                            Component = tool.ToolName ?? "unknown",
                            Severity = "High",
                            Description = $"Tool '{tool.ToolName}' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: '{pattern}').",
                            Mitigation = "Validate all URIs/URLs server-side. Restrict outbound connections to allowlisted domains."
                        });
                        break;
                    }
                }
            }
        }

        // ─── Boundary Check: Prompt Injection Surface ───────────────
        if (result.ToolValidation?.ToolResults != null)
        {
            foreach (var tool in result.ToolValidation.ToolResults)
            {
                var promptSurfaceTexts = new[] { tool.DisplayTitle, tool.Description }
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text!.ToLowerInvariant())
                    .ToList();

                foreach (var surfaceText in promptSurfaceTexts)
                {
                    foreach (var pattern in ScoringConstants.PromptInjectionPatterns)
                    {
                        if (ContainsPromptInjectionPattern(surfaceText, pattern))
                        {
                            assessment.PromptInjectionSurfaceCount++;
                            assessment.BoundaryFindings.Add(new AiBoundaryFinding
                            {
                                Category = "PromptInjection",
                                Component = tool.ToolName,
                                Severity = "Critical",
                                Description = $"Tool '{tool.ToolName}' metadata contains prompt-injection-like language: '{pattern}'.",
                                Mitigation = "Remove instruction-like language from tool descriptions. Descriptions should be factual, not imperative."
                            });
                            break;
                        }
                    }
                }
            }
        }

        // ─── Boundary Check: Injection Reflection ───────────────────
        if (result.SecurityTesting?.AttackSimulations != null)
        {
            var reflectedCount = result.SecurityTesting.AttackSimulations.Count(a => a.AttackSuccessful);
            if (reflectedCount > 0)
            {
                score -= reflectedCount * 10;
                assessment.BoundaryFindings.Add(new AiBoundaryFinding
                {
                    Category = "Injection",
                    Component = "SecurityValidator",
                    Severity = "High",
                    Description = $"{reflectedCount} injection attack(s) reflected back in server response. AI agents consuming this output may execute malicious content.",
                    Mitigation = "Sanitize all tool outputs. Never reflect user input directly in response content."
                });
            }
        }

        // ─── Penalties ──────────────────────────────────────────────
        score -= ValidationCalibration.CalculateRelativeExposurePenalty(
            assessment.DataExfiltrationRiskCount,
            toolCatalogSize,
            maxPenalty: 15,
            minimumPenaltyIfAny: 3);

        score -= ValidationCalibration.CalculateRelativeExposurePenalty(
            assessment.PromptInjectionSurfaceCount,
            toolCatalogSize,
            maxPenalty: 18,
            minimumPenaltyIfAny: 4);

        // ─── LLM-Friendliness: Extract from tool issues ─────────────
        // Parse LLM-Friendliness scores from tool result issues and average them.
        // This measures whether error responses help AI agents self-correct.
        if (result.ToolValidation?.ToolResults != null)
        {
            var llmScores = new List<int>();
            foreach (var tool in result.ToolValidation.ToolResults)
            {
                foreach (var finding in tool.Findings.Where(f => f.RuleId == ValidationFindingRuleIds.ToolLlmFriendliness))
                {
                    if (finding.Metadata.TryGetValue("score", out var scoreText) && int.TryParse(scoreText, out var llmScore))
                    {
                        llmScores.Add(llmScore);
                    }
                }

                if (tool.Findings.Any(f => f.RuleId == ValidationFindingRuleIds.ToolLlmFriendliness))
                {
                    continue;
                }

                foreach (var issue in tool.Issues)
                {
                    if (issue.Contains("LLM-Friendliness:"))
                    {
                        var start = issue.IndexOf("LLM-Friendliness:") + "LLM-Friendliness:".Length;
                        var end = issue.IndexOf("/100", start);
                        if (end > start && int.TryParse(issue.Substring(start, end - start).Trim(), out var llmScore))
                        {
                            llmScores.Add(llmScore);
                        }
                    }
                }
            }

            if (llmScores.Count > 0)
            {
                assessment.LlmFriendlinessScore = Math.Round(llmScores.Average(), 1);

                // Anti-LLM servers get penalized in AI Safety
                if (assessment.LlmFriendlinessScore < 40)
                {
                    score -= 15;
                    assessment.BoundaryFindings.Add(new AiBoundaryFinding
                    {
                        Category = "LLM-Hostile",
                        Component = "Error Responses",
                        Severity = "High",
                        Description = $"Average LLM-friendliness score is {assessment.LlmFriendlinessScore}/100 (Anti-LLM). Error messages don't help AI agents self-correct, causing hallucination and retry loops.",
                        Mitigation = "Return structured errors with: specific parameter names, expected types/formats, and use standard JSON-RPC error codes (-32602 for invalid params)."
                    });
                }
                else if (assessment.LlmFriendlinessScore < 70)
                {
                    score -= 5;
                }
            }
        }

        return Math.Max(0, Math.Min(100, Math.Round(score, 1)));
    }

    private static bool ContainsPromptInjectionPattern(string surfaceText, string pattern)
    {
        if (string.IsNullOrWhiteSpace(surfaceText) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (!SentenceLeadingPromptInjectionPatterns.Contains(pattern))
        {
            return surfaceText.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var segment in surfaceText.Split(['\r', '\n', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var bulletSegment in segment.Split(['-', '*'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (bulletSegment.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasFinding(IEnumerable<ValidationFinding>? findings, string ruleId)
    {
        return findings?.Any(f => f.RuleId == ruleId) == true;
    }

    private static bool HasViolation(IEnumerable<ComplianceViolation>? violations, string checkId)
    {
        return violations?.Any(v => string.Equals(v.CheckId, checkId, StringComparison.Ordinal)) == true;
    }

    private static McpTrustLevel? GetProtocolSecurityCap(McpTrustAssessment assessment)
    {
        var anchor = Math.Min(assessment.ProtocolCompliance, assessment.SecurityPosture);

        if (anchor < ScoringConstants.TrustL2Threshold)
        {
            return McpTrustLevel.L1_Untrusted;
        }

        if (anchor < ScoringConstants.TrustL3Threshold)
        {
            return McpTrustLevel.L2_Caution;
        }

        if (anchor < ScoringConstants.TrustL4Threshold)
        {
            return McpTrustLevel.L3_Acceptable;
        }

        if (anchor < ScoringConstants.TrustL5Threshold)
        {
            return McpTrustLevel.L4_Trusted;
        }

        return null;
    }
}
