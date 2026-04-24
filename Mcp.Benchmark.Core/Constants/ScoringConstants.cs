namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Centralized scoring constants for MCP Validator.
/// All magic numbers, thresholds, and weights live here — nowhere else.
/// Change these values to adjust scoring sensitivity.
/// </summary>
public static class ScoringConstants
{
    // ─── Trust Level Thresholds ──────────────────────────────────────
    // The weighted trust score maps to a level and may then be capped by blocking findings.
    
    /// <summary>Weighted trust score >= 90 before any blocking caps → L5 Certified Secure</summary>
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

    // ─── AI Boundary Detection ──────────────────────────────────────

    /// <summary>
    /// Patterns in tool parameter names/descriptions that indicate potential 
    /// data exfiltration risk (tools that accept URIs/URLs that could be manipulated).
    /// </summary>
    public static readonly string[] ExfiltrationRiskPatterns = 
    {
        "url", "uri", "endpoint", "webhook", "callback", "redirect",
        "destination", "target", "forward", "proxy", "fetch"
    };

    /// <summary>
    /// Patterns in tool/prompt descriptions that could be used for prompt injection
    /// (instruction-like language that could override AI system prompts).
    /// </summary>
    public static readonly string[] PromptInjectionPatterns =
    {
        "ignore previous", "disregard", "override", "system prompt",
        "you are", "act as", "pretend", "forget instructions"
    };
}
