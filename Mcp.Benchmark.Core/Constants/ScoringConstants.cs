using System.Collections.Immutable;

namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Centralized scoring constants for MCP Validator.
/// All magic numbers, thresholds, and weights live here — nowhere else.
/// Change these values to adjust scoring sensitivity.
/// </summary>
public static class ScoringConstants
{
    public const string PolicyVersion = "2026-08-19.2";

    // ─── Trust Level Thresholds ──────────────────────────────────────
    // The weighted trust score maps to a level and may then be capped by blocking findings.
    
    /// <summary>Weighted trust score >= 90 before any blocking caps → L5 High Assurance</summary>
    public const double TrustL5Threshold = 90.0;
    
    /// <summary>Weighted trust score >= 75 before any blocking caps → L4 Trusted</summary>
    public const double TrustL4Threshold = 75.0;
    
    /// <summary>Weighted trust score >= 50 after applying caps → L3 Acceptable</summary>
    public const double TrustL3Threshold = 50.0;
    
    /// <summary>Weighted trust score >= 25 or capped into caution range → L2 Caution</summary>
    public const double TrustL2Threshold = 25.0;
    
    // Below L2 → L1 Untrusted

    // ─── Trust Dimension Weights ───────────────────────────────────

    public const double TrustWeightProtocol = 0.30;
    public const double TrustWeightSecurity = 0.35;
    public const double TrustWeightAiSafety = 0.20;
    public const double TrustWeightOperations = 0.15;

    // ─── Category Weights (Aggregate Scoring) ────────────────────────
    
    public const double WeightProtocol = 0.30;
    public const double WeightSecurity = 0.45;
    public const double WeightTools = 0.10;
    public const double WeightResources = 0.05;
    public const double WeightPrompts = 0.05;
    public const double WeightErrorHandling = 0.05;
    public const double WeightPerformance = 0.00; // Informational only — correctness > speed

    // ─── Coverage Thresholds ─────────────────────────────────────────
    
    /// <summary>Minimum coverage ratio before score is capped.</summary>
    public const double MinCoverageRatio = 0.50;
    
    /// <summary>Maximum score when coverage is below MinCoverageRatio.</summary>
    public const double LowCoverageScoreCap = 60.0;

    // ─── Pass/Fail Thresholds ────────────────────────────────────────
    
    /// <summary>Score >= this → Passed</summary>
    public const double PassThreshold = 70.0;
    
    /// <summary>Score >= this → Passed with no warnings</summary>
    public const double ExcellentThreshold = 90.0;

    // ─── Performance Scoring ─────────────────────────────────────────
    
    /// <summary>Baseline latency in ms (no penalty below this).</summary>
    public const double LatencyBaselineMs = 200.0;
    
    /// <summary>Latency penalty: 1 point deducted per this many ms over baseline.</summary>
    public const double LatencyPenaltyPerMs = 20.0;
    
    /// <summary>Points deducted per failed request in load test.</summary>
    public const double FailedRequestPenalty = 5.0;

    // ─── Security Scoring ────────────────────────────────────────────
    
    /// <summary>Score penalty per Critical vulnerability.</summary>
    public const double VulnPenaltyCritical = 25.0;
    
    /// <summary>Score penalty per High vulnerability.</summary>
    public const double VulnPenaltyHigh = 15.0;
    
    /// <summary>Score penalty per Medium vulnerability.</summary>
    public const double VulnPenaltyMedium = 8.0;
    
    /// <summary>Score penalty per Low vulnerability.</summary>
    public const double VulnPenaltyLow = 3.0;
    
    /// <summary>Score penalty per Informational vulnerability.</summary>
    public const double VulnPenaltyInfo = 1.0;
    
    /// <summary>Maximum points deducted for auth protocol violations.</summary>
    public const double MaxAuthProtocolPenalty = 20.0;
    
    /// <summary>Points deducted per auth protocol violation.</summary>
    public const double AuthViolationPenalty = 5.0;
    
    /// <summary>Points deducted for JSON-RPC compliance violations.</summary>
    public const double JsonRpcViolationPenalty = 15.0;

    // ─── AI Safety Scoring ──────────────────────────────────────────
    
    /// <summary>Penalty per undescribed parameter (as fraction of max 30pt penalty).</summary>
    public const double AiDescriptionPenaltyMax = 30.0;
    
    /// <summary>Penalty per unconstrained string parameter (as fraction of max 20pt penalty).</summary>
    public const double AiVagueTypePenaltyMax = 20.0;
    
    /// <summary>Token count threshold for warning.</summary>
    public const long TokenWarningThreshold = 8000;
    
    /// <summary>Token count threshold for penalty.</summary>
    public const long TokenPenaltyThreshold = 32000;
    
    /// <summary>Score penalty when token count exceeds penalty threshold.</summary>
    public const double TokenExcessPenalty = 10.0;
    
    /// <summary>Approximate chars per token for JSON estimation.</summary>
    public const int CharsPerToken = 4;

    public const double ExfiltrationPenaltyMax = 15.0;

    public const double ExfiltrationPenaltyMinimum = 3.0;

    public const double PromptInjectionPenaltyMax = 18.0;

    public const double PromptInjectionPenaltyMinimum = 4.0;

    public const double CoverageConfidenceHigh = 1.0;

    public const double CoverageConfidenceMedium = 0.65;

    public const double CoverageConfidenceLow = 0.35;

    public const double CoverageConfidenceSkippedDisabled = 0.25;

    public const double CoverageConfidenceInconclusive = 0.20;

    public const double ConfidenceLevelHighThreshold = 0.85;

    public const double ConfidenceLevelMediumThreshold = 0.60;

    public const double ScoreMinimum = 0.0;

    public const double ScoreMaximum = 100.0;

    public const double BlockingAuthenticationScoreCap = 20.0;

    public const double CriticalVulnerabilityScoreCap = 30.0;

    public const double SevereSecurityThreshold = 25.0;

    public const double SevereSecurityScoreCap = 25.0;

    public const double AuthGuidancePenaltyMaximum = 20.0;

    public const double AuthGuidancePenaltyPerScenario = 5.0;

    public const double CriticalProtocolScoreCap = 40.0;

    public const double JsonRpcFormatPenalty = 15.0;

    public const double InjectionReflectionPenaltyPerFinding = 10.0;

    public const double LlmHostileThreshold = 40.0;

    public const double LlmHostilePenalty = 15.0;

    public const double LlmGuidanceThreshold = 70.0;

    public const double LlmGuidancePenalty = 5.0;

    public const double SecurityDefaultPenalty = 5.0;

    public const double SecurityControlsRecommendationThreshold = 80.0;

    public const double StandardsAlignedScenarioScore = 100.0;

    public const double SecureCompatibleScenarioScore = 75.0;

    public const double AdvisoryPerformanceScore = 70.0;

    public const double AiReadinessEmptySchemaScore = 80.0;

    public const double AiRequiredArrayPenaltyMax = 15.0;

    public const double AiEnumCoveragePenaltyMax = 12.0;

    public const double AiFormatHintPenaltyMax = 12.0;

    public const int ToolErrorUpstreamPassThroughScore = 60;
    public const int ToolErrorStandardCodePoints = 20;
    public const int ToolErrorParameterPoints = 25;
    public const int ToolErrorExpectedFormatPoints = 20;
    public const int ToolErrorDataPoints = 15;
    public const int ToolErrorMessageContextPoints = 10;
    public const int ToolErrorIsErrorPoints = 10;
    public const int ToolErrorMessageMinimumLength = 20;
    public const int ToolErrorHelpfulThreshold = 70;
    public const int ToolErrorNeutralThreshold = 40;
    public const int ToolErrorSupportingIssueLimit = 2;

    public const string UpstreamHttpStatusPattern = @"\b(?:HTTP\s+)?[1-5]\d{2}\b\s*(?:Not\s+Found|Bad\s+Request|Unauthorized|Forbidden|Conflict|Unprocessable|Gone|Too\s+Many\s+Requests|Internal\s+Server\s+Error|Service\s+Unavailable|Gateway)\b";
    public const string UpstreamMethodUrlPattern = @"\b(?:GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+https?://[^\s""]+";

    public const double ContentPublicAnonymousHighScore = 95.0;
    public const double ContentPublicAnonymousOtherScore = 75.0;
    public const double ContentPublicAuthenticatedHighScore = 90.0;
    public const double ContentPublicAuthenticatedOtherScore = 65.0;
    public const double ContentGovernedFallbackScore = 70.0;
    public const double ContentEnterpriseUncontrolledHighScore = 88.0;
    public const double ContentEnterpriseUncontrolledOtherScore = 62.0;
    public const double ContentCiSystemHighScore = 92.0;
    public const double ContentCiSystemOtherScore = 68.0;
    public const double ContentInternalGovernedFallbackScore = 72.0;
    public const double ContentInternalFloorScore = 55.0;
    public const double ContentInternalHighCap = 85.0;
    public const double ContentInternalOtherCap = 65.0;
    public const double ContentRiskHighFloor = 88.0;
    public const double ContentRiskMediumCap = 70.0;
    public const double ContentRiskLowCap = 40.0;

    public const int ContentArgumentRichPromptThreshold = 5;

    public const double ContentArgumentRichPromptScore = 65.0;

    public const double ContentKeywordHighScore = 90.0;

    public const double ContentKeywordMediumScore = 60.0;

    public const int AiSemanticGuidanceMinimumLength = 8;

    public static ImmutableArray<string> AiEnumeratedChoiceMarkers { get; } =
    [
        "mode", "type", "kind", "status", "state", "level", "action", "scope", "sort", "order", "direction"
    ];

    public static ImmutableArray<string> AiStructuredFormatMarkers { get; } =
    [
        "url", "uri", "email", "date", "time", "timestamp", "uuid", "guid", "hostname", "domain"
    ];

    public static ImmutableArray<string> ContentSafetySystemImpactHighKeywords { get; } =
    [
        "delete", "remove", "destroy", "drop", "truncate", "wipe",
        "shutdown", "terminate", "kill", "lock", "ban", "execute", "exec",
        "shell", "command", "powershell", "bash", "admin", "root"
    ];

    public static ImmutableArray<string> ContentSafetySystemImpactMediumKeywords { get; } =
    [
        "update", "modify", "change", "set", "write", "patch"
    ];

    public static ImmutableArray<string> ContentSafetyDataExfiltrationHighKeywords { get; } =
    [
        "dump", "download", "export", "backup", "snapshot", "all-data", "fulldump", "full-dump"
    ];

    public static ImmutableArray<string> ContentSafetyDataExfiltrationMediumKeywords { get; } =
    [
        "list-all", "history", "logs", "audit", "report"
    ];

    public static ImmutableArray<string> ContentSafetyAbuseHighKeywords { get; } =
    [
        "broadcast", "notify-all", "email-all", "message-all", "spam", "bulk-send"
    ];

    public static ImmutableArray<string> ContentSafetyAbuseMediumKeywords { get; } =
    [
        "notify", "email", "message", "post"
    ];

    // ─── AI Boundary Detection ──────────────────────────────────────

    /// <summary>
    /// Parameter-name patterns that indicate a tool accepts an outbound target
    /// or remote location supplied by the caller.
    /// </summary>
    public static ImmutableArray<string> ExfiltrationTargetParameterPatterns { get; } =
    [
        "url", "uri", "endpoint", "webhook", "callback", "redirect",
        "destination", "target"
    ];

    /// <summary>
    /// Behavior patterns that suggest a tool can forward, proxy, or otherwise
    /// send data to a caller-controlled destination.
    /// </summary>
    public static ImmutableArray<string> ExfiltrationBehaviorPatterns { get; } =
    [
        "webhook", "callback", "redirect", "destination", "forward",
        "proxy", "upload", "post", "send", "push", "notify", "export"
    ];

    /// <summary>
    /// Patterns in tool/prompt descriptions that could be used for prompt injection
    /// (instruction-like language that could override AI system prompts).
    /// </summary>
    public static ImmutableArray<string> PromptInjectionPatterns { get; } =
    [
        "ignore previous", "disregard", "override", "system prompt",
        "you are", "act as", "pretend", "forget instructions"
    ];

    public static ImmutableArray<string> SentenceLeadingPromptInjectionPatterns { get; } =
    [
        "you are", "act as", "pretend"
    ];

    public static ImmutableArray<string> DestructiveToolNamePatterns { get; } =
    [
        "delete", "remove", "drop", "destroy", "write", "update",
        "create", "execute", "run", "send"
    ];
}
