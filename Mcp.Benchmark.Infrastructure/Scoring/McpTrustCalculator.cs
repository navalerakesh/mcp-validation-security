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

    public static void ApplyAuthoritativeVerdictCap(McpTrustAssessment assessment, VerdictAssessment? verdictAssessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (verdictAssessment is not { BaselineVerdict: ValidationVerdict.Reject } &&
            verdictAssessment is not { ProtocolVerdict: ValidationVerdict.Reject })
        {
            return;
        }

        if (assessment.TrustLevel > McpTrustLevel.L2_Caution)
        {
            assessment.TrustLevel = McpTrustLevel.L2_Caution;
        }
    }

    public static McpTrustAssessment Calculate(ValidationResult result)
    {
        var assessment = new McpTrustAssessment();

        // ─── Run Tiered Compliance Checks ────────────────────────────
        RunMustChecks(result, assessment);
        RunShouldChecks(result, assessment);
        RunMayChecks(result, assessment);

        var protocolEvaluated = IsEvaluated(result.ProtocolCompliance?.Status) &&
            result.ProtocolCompliance?.JsonRpcCompliance.ErrorHandlingEvaluated == true;
        var securityEvaluated = IsEvaluated(result.SecurityTesting?.Status);
        var aiSafetyEvaluated = IsEvaluated(result.ToolValidation?.Status) ||
            IsEvaluated(result.ResourceTesting?.Status) ||
            IsEvaluated(result.PromptTesting?.Status);
        var operationsEvaluated = IsEvaluated(result.PerformanceTesting?.Status);

        // ─── Dimension 1: Protocol Compliance ────────────────────────
        assessment.ProtocolCompliance = result.ProtocolCompliance?.ComplianceScore ?? 0;

        // ─── Dimension 2: Security Posture ───────────────────────────
        assessment.SecurityPosture = result.SecurityTesting?.SecurityScore ?? 0;

        // ─── Dimension 3: AI Safety ─────────────────────────────────
        assessment.AiSafety = CalculateAiSafety(result, assessment);

        // ─── Dimension 4: Operational Readiness ──────────────────────
        assessment.OperationalReadiness = ValidationCalibration.GetOperationalReadinessScore(result.ServerConfig, result.PerformanceTesting);

        AddUnevaluatedDimension(assessment, protocolEvaluated, "protocol");
        AddUnevaluatedDimension(assessment, securityEvaluated, "security");
        AddUnevaluatedDimension(assessment, aiSafetyEvaluated, "aiSafety");
        AddUnevaluatedDimension(assessment, operationsEvaluated, "operations");

        // ─── Determine Trust Level ───────────────────────────────────
        if (ValidationCalibration.HasBlockingSecurityFailure(result) || result.CriticalErrors.Count > 0)
        {
            assessment.TrustLevel = McpTrustLevel.L1_Untrusted;
            return assessment;
        }

        var evaluatedWeight = 0.0;
        var weightedScore = 0.0;
        AddEvaluatedDimension(protocolEvaluated, assessment.ProtocolCompliance, ScoringConstants.TrustWeightProtocol, ref evaluatedWeight, ref weightedScore);
        AddEvaluatedDimension(securityEvaluated, assessment.SecurityPosture, ScoringConstants.TrustWeightSecurity, ref evaluatedWeight, ref weightedScore);
        AddEvaluatedDimension(aiSafetyEvaluated, assessment.AiSafety, ScoringConstants.TrustWeightAiSafety, ref evaluatedWeight, ref weightedScore);
        AddEvaluatedDimension(operationsEvaluated, assessment.OperationalReadiness, ScoringConstants.TrustWeightOperations, ref evaluatedWeight, ref weightedScore);

        assessment.EvidenceCompletenessRatio = evaluatedWeight;
        if (evaluatedWeight <= 0)
        {
            assessment.TrustLevel = McpTrustLevel.Unknown;
            assessment.LimitedByIncompleteEvidence = true;
            return assessment;
        }

        var weightedTrustScore = weightedScore / evaluatedWeight;

        var calculatedLevel = weightedTrustScore switch
        {
            >= ScoringConstants.TrustL5Threshold => McpTrustLevel.L5_CertifiedSecure,
            >= ScoringConstants.TrustL4Threshold => McpTrustLevel.L4_Trusted,
            >= ScoringConstants.TrustL3Threshold => McpTrustLevel.L3_Acceptable,
            >= ScoringConstants.TrustL2Threshold => McpTrustLevel.L2_Caution,
            _ => McpTrustLevel.L1_Untrusted
        };

        var protocolSecurityCap = GetProtocolSecurityCap(assessment, protocolEvaluated, securityEvaluated);
        if (protocolSecurityCap.HasValue && calculatedLevel > protocolSecurityCap.Value)
        {
            calculatedLevel = protocolSecurityCap.Value;
        }

        var activeProtocolEvidenceMissing = result.ProtocolCompliance?.JsonRpcCompliance?.ErrorHandlingEvaluated == false;
        if (assessment.UnevaluatedDimensions.Count > 0 || activeProtocolEvidenceMissing)
        {
            assessment.LimitedByIncompleteEvidence = true;
            if (calculatedLevel > McpTrustLevel.L3_Acceptable)
            {
                calculatedLevel = McpTrustLevel.L3_Acceptable;
            }
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

    private static bool IsEvaluated(TestStatus? status) => status is TestStatus.Passed or TestStatus.Failed;

    private static void AddUnevaluatedDimension(McpTrustAssessment assessment, bool evaluated, string dimension)
    {
        if (!evaluated)
        {
            assessment.UnevaluatedDimensions.Add(dimension);
        }
    }

    private static void AddEvaluatedDimension(
        bool evaluated,
        double score,
        double weight,
        ref double evaluatedWeight,
        ref double weightedScore)
    {
        if (!evaluated)
        {
            return;
        }

        evaluatedWeight += weight;
        weightedScore += score * weight;
    }

    // ─── MUST Checks: Hard Compliance Gates ──────────────────────────

    private static void RunMustChecks(ValidationResult result, McpTrustAssessment assessment)
    {
        // Protocol MUST checks
        if (IsEvaluated(result.ProtocolCompliance?.Status))
        {
            AddMustCheck(assessment, McpComplianceTiers.Must.InitializeResponse, "initialize",
                !HasViolation(result.ProtocolCompliance?.Violations, ValidationConstants.CheckIds.ProtocolInitializeResponse),
                HasViolation(result.ProtocolCompliance?.Violations, ValidationConstants.CheckIds.ProtocolInitializeResponse) ? "Initialize failed" : null);
        }

        // Only check response structure if we got a successful response
        if (result.ProtocolCompliance != null && IsEvaluated(result.ProtocolCompliance.Status))
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
        if (result.ToolValidation != null && IsEvaluated(result.ToolValidation.Status))
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
        if (result.ResourceTesting != null && IsEvaluated(result.ResourceTesting.Status))
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
        if (result.PromptTesting != null && IsEvaluated(result.PromptTesting.Status))
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
            var protocolErrorHandling = result.ProtocolCompliance?.JsonRpcCompliance;
            var protocolErrorHandlingFailed = protocolErrorHandling?.ErrorHandlingEvaluated == true &&
                                              protocolErrorHandling.ErrorHandlingCompliant == false;
            var errorHandlingFindings = result.ErrorHandling?.Findings?
                .Where(f => string.Equals(f.RuleId, "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE", StringComparison.Ordinal))
                .ToList() ?? new List<ValidationFinding>();
            var standardErrorCodesEvaluated = protocolErrorHandling?.ErrorHandlingEvaluated == true || errorHandlingFindings.Count > 0;
            var standardErrorCodesPassed = standardErrorCodesEvaluated && !protocolErrorHandlingFailed && errorHandlingFindings.Count == 0;
            var standardErrorCodeDetail = protocolErrorHandlingFailed
                ? "Non-standard error codes"
                : errorHandlingFindings.Count > 0
                    ? $"Non-standard error codes in: {string.Join(", ", errorHandlingFindings.Select(f => f.Component).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))}"
                    : null;

            if (standardErrorCodesEvaluated)
            {
                AddMustCheck(assessment, McpComplianceTiers.Must.StandardErrorCodes, "errors",
                    standardErrorCodesPassed,
                    standardErrorCodeDetail);
            }
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

        // NOTE: MCP spec marks tools/call result.isError as OPTIONAL (omitted == false).
        // Do not score its absence as a SHOULD violation; explicit isError:true is observed via
        // unsafe-call evidence elsewhere (attack simulations / error-handling findings).

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
            var reflected = result.SecurityTesting.AttackSimulations.Any(attack =>
                AttackSimulationOutcomeResolver.Resolve(attack) == AttackSimulationOutcome.Detected);
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

        if (result.ResourceTesting != null)
        {
            AddMayCheck(
                assessment,
                McpComplianceTiers.May.ResourceTemplates,
                "resources",
                result.ResourceTesting.ResourceTemplatesDiscovered > 0);
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
        double score = ScoringConstants.ScoreMaximum;
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
                                           ScoringConstants.DestructiveToolNamePatterns.Any(pattern =>
                                               name.Contains(pattern, StringComparison.Ordinal)));

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
                        Kind = AiBoundaryKind.DestructiveOperation,
                        Component = tool.ToolName ?? "unknown",
                        Severity = severity,
                        SeverityLevel = tool.DestructiveHint == true ? ValidationFindingSeverity.High : ValidationFindingSeverity.Medium,
                        Gate = GateOutcome.ReviewRequired,
                        ImpactAreas = [ImpactArea.UnsafeAutonomy],
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

                var matchedParameterPattern = ScoringConstants.ExfiltrationTargetParameterPatterns
                    .FirstOrDefault(pattern => parameterNames.Any(parameter => parameter.Contains(pattern, StringComparison.Ordinal)));

                if (matchedParameterPattern is null)
                {
                    continue;
                }

                var matchedBehaviorPattern = ScoringConstants.ExfiltrationBehaviorPatterns
                    .FirstOrDefault(pattern =>
                        name.Contains(pattern, StringComparison.Ordinal) ||
                        descriptionText.Contains(pattern, StringComparison.Ordinal));

                var hasOpenWorldWriteBehavior = tool.OpenWorldHint == true && tool.ReadOnlyHint != true;
                if (matchedBehaviorPattern is null && !hasOpenWorldWriteBehavior)
                {
                    continue;
                }

                var evidence = matchedBehaviorPattern ?? matchedParameterPattern;
                assessment.DataExfiltrationRiskCount++;
                assessment.BoundaryFindings.Add(new AiBoundaryFinding
                {
                    Category = "Exfiltration",
                    Kind = AiBoundaryKind.DataExfiltration,
                    Component = tool.ToolName ?? "unknown",
                    Severity = "High",
                    SeverityLevel = ValidationFindingSeverity.High,
                    Gate = GateOutcome.Reject,
                    ImpactAreas = [ImpactArea.DataExposure, ImpactArea.UnsafeAutonomy],
                    Description = $"Tool '{tool.ToolName}' accepts caller-controlled outbound targets and shows egress behavior that could enable data exfiltration (evidence: '{evidence}').",
                    Mitigation = "Validate outbound destinations server-side. Restrict network targets to explicit allowlists and require least-privilege access."
                });
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
                                Kind = AiBoundaryKind.PromptInjection,
                                Component = tool.ToolName,
                                Severity = "Critical",
                                SeverityLevel = ValidationFindingSeverity.Critical,
                                Gate = GateOutcome.Reject,
                                ImpactAreas = [ImpactArea.UnsafeAutonomy, ImpactArea.OutputIntegrity],
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
            var reflectedCount = result.SecurityTesting.AttackSimulations.Count(attack =>
                AttackSimulationOutcomeResolver.Resolve(attack) == AttackSimulationOutcome.Detected);
            if (reflectedCount > 0)
            {
                score -= reflectedCount * ScoringConstants.InjectionReflectionPenaltyPerFinding;
                assessment.BoundaryFindings.Add(new AiBoundaryFinding
                {
                    Category = "Injection",
                    Kind = AiBoundaryKind.InjectionReflection,
                    Component = "SecurityValidator",
                    Severity = "High",
                    SeverityLevel = ValidationFindingSeverity.High,
                    Gate = GateOutcome.Reject,
                    ImpactAreas = [ImpactArea.OutputIntegrity, ImpactArea.UnsafeAutonomy],
                    Description = $"{reflectedCount} injection attack(s) reflected back in server response. AI agents consuming this output may execute malicious content.",
                    Mitigation = "Sanitize all tool outputs. Never reflect user input directly in response content."
                });
            }
        }

        // ─── Penalties ──────────────────────────────────────────────
        score -= ValidationCalibration.CalculateRelativeExposurePenalty(
            assessment.DataExfiltrationRiskCount,
            toolCatalogSize,
            maxPenalty: ScoringConstants.ExfiltrationPenaltyMax,
            minimumPenaltyIfAny: ScoringConstants.ExfiltrationPenaltyMinimum);

        score -= ValidationCalibration.CalculateRelativeExposurePenalty(
            assessment.PromptInjectionSurfaceCount,
            toolCatalogSize,
            maxPenalty: ScoringConstants.PromptInjectionPenaltyMax,
            minimumPenaltyIfAny: ScoringConstants.PromptInjectionPenaltyMinimum);

        // ─── LLM-Friendliness: Extract from tool issues ─────────────
        // Parse LLM-Friendliness scores from tool result issues and average them.
        // This measures whether error responses help AI agents self-correct.
        if (result.ToolValidation?.ToolResults != null)
        {
            var llmScores = new List<double>();
            var hasInvalidLlmScoreEvidence = false;
            foreach (var tool in result.ToolValidation.ToolResults)
            {
                foreach (var finding in tool.Findings.Where(f => f.RuleId == ValidationFindingRuleIds.ToolLlmFriendliness))
                {
                    if (finding.ExcludedFromAggregate)
                    {
                        continue;
                    }

                    if (finding.Score is >= ScoringConstants.ScoreMinimum and <= ScoringConstants.ScoreMaximum)
                    {
                        llmScores.Add(finding.Score.Value);
                    }
                    else
                    {
                        hasInvalidLlmScoreEvidence = true;
                    }
                }
            }

            if (hasInvalidLlmScoreEvidence)
            {
                score -= ScoringConstants.LlmGuidancePenalty;
                assessment.BoundaryFindings.Add(new AiBoundaryFinding
                {
                    Category = "LLM-Evidence-Inconclusive",
                    Kind = AiBoundaryKind.LlmHostileErrors,
                    Component = "Error Responses",
                    Severity = "Medium",
                    SeverityLevel = ValidationFindingSeverity.Medium,
                    Gate = GateOutcome.ReviewRequired,
                    ImpactAreas = [ImpactArea.RecoveryIntegrity],
                    Description = "LLM-friendliness evidence was present without a valid typed score.",
                    Mitigation = "Emit a typed LLM-friendliness score between 0 and 100 or explicitly exclude the finding from aggregation."
                });
            }

            if (llmScores.Count > 0)
            {
                assessment.LlmFriendlinessScore = Math.Round(llmScores.Average(), 1);

                // Anti-LLM servers get penalized in AI Safety
                if (assessment.LlmFriendlinessScore < ScoringConstants.LlmHostileThreshold)
                {
                    score -= ScoringConstants.LlmHostilePenalty;
                    assessment.BoundaryFindings.Add(new AiBoundaryFinding
                    {
                        Category = "LLM-Hostile",
                        Kind = AiBoundaryKind.LlmHostileErrors,
                        Component = "Error Responses",
                        Severity = "High",
                        SeverityLevel = ValidationFindingSeverity.High,
                        Gate = GateOutcome.ReviewRequired,
                        ImpactAreas = [ImpactArea.RecoveryIntegrity, ImpactArea.UnsafeAutonomy],
                        Description = $"Average LLM-friendliness score is {assessment.LlmFriendlinessScore}/100 (Anti-LLM). Error messages don't help AI agents self-correct, causing hallucination and retry loops.",
                        Mitigation = "Return structured errors with: specific parameter names, expected types/formats, and use standard JSON-RPC error codes (-32602 for invalid params)."
                    });
                }
                else if (assessment.LlmFriendlinessScore < ScoringConstants.LlmGuidanceThreshold)
                {
                    score -= ScoringConstants.LlmGuidancePenalty;
                }
            }
        }

        return Math.Max(ScoringConstants.ScoreMinimum, Math.Min(ScoringConstants.ScoreMaximum, Math.Round(score, 1)));
    }

    private static bool ContainsPromptInjectionPattern(string surfaceText, string pattern)
    {
        if (string.IsNullOrWhiteSpace(surfaceText) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (!ScoringConstants.SentenceLeadingPromptInjectionPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            return surfaceText.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

            foreach (var segment in surfaceText.Split(new[] { '\r', '\n', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

                foreach (var bulletSegment in segment.Split(new[] { '-', '*' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    private static McpTrustLevel? GetProtocolSecurityCap(
        McpTrustAssessment assessment,
        bool protocolEvaluated,
        bool securityEvaluated)
    {
        var evaluatedAnchors = new List<double>(2);
        if (protocolEvaluated)
        {
            evaluatedAnchors.Add(assessment.ProtocolCompliance);
        }

        if (securityEvaluated)
        {
            evaluatedAnchors.Add(assessment.SecurityPosture);
        }

        if (evaluatedAnchors.Count == 0)
        {
            return null;
        }

        var anchor = evaluatedAnchors.Min();

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
