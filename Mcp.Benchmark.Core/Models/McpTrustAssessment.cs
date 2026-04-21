namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// MCP Trust Level — a multi-dimensional assessment of how trustworthy an MCP server is
/// for consumption by AI agents. This goes BEYOND protocol compliance to measure whether
/// an AI agent can safely and reliably use this server.
///
/// Dimensions:
/// 1. Protocol Compliance — Does the server follow MCP spec correctly?
/// 2. Security Posture — Is the server resistant to attacks and properly authenticated?
/// 3. AI Safety — Can an AI agent safely consume this server without hallucination/exfiltration risks?
/// 4. Operational Readiness — Is the server performant, stable, and production-ready?
/// </summary>
public class McpTrustAssessment
{
    /// <summary>
    /// Overall trust level (L1-L5). Derived from a weighted multi-dimensional score and capped by confirmed blockers.
    /// </summary>
    public McpTrustLevel TrustLevel { get; set; } = McpTrustLevel.Unknown;

    /// <summary>
    /// Human-readable trust label.
    /// </summary>
    public string TrustLabel => TrustLevel switch
    {
        McpTrustLevel.L5_CertifiedSecure => "L5: Certified Secure — Production AI-agent ready",
        McpTrustLevel.L4_Trusted => "L4: Trusted — Meets enterprise AI safety requirements",
        McpTrustLevel.L3_Acceptable => "L3: Acceptable — Compliant with known limitations",
        McpTrustLevel.L2_Caution => "L2: Caution — Significant gaps in safety or compliance",
        McpTrustLevel.L1_Untrusted => "L1: Untrusted — Critical failures, not safe for AI agents",
        _ => "Unknown — Insufficient data to assess"
    };

    // ─── Dimension Scores (0-100) ────────────────────────────────────

    /// <summary>
    /// Protocol Compliance (0-100): Does the server implement MCP spec correctly?
    /// Sources: JSON-RPC compliance, initialize handshake, response structures, pagination.
    /// </summary>
    public double ProtocolCompliance { get; set; }

    /// <summary>
    /// Security Posture (0-100): Is the server resistant to attacks?
    /// Sources: Auth compliance, injection resistance, error smuggling, metadata enumeration.
    /// </summary>
    public double SecurityPosture { get; set; }

    /// <summary>
    /// AI Safety (0-100): Can an AI agent safely consume this server?
    /// Sources: Schema quality (hallucination risk), destructive tool detection,
    /// data exfiltration surface, prompt injection resistance, token efficiency.
    /// </summary>
    public double AiSafety { get; set; }

    /// <summary>
    /// Operational Readiness (0-100): Is the server production-ready?
    /// Sources: Latency, throughput, error rate, stability under load.
    /// </summary>
    public double OperationalReadiness { get; set; }

    // ─── AI Boundary Checks ──────────────────────────────────────────

    /// <summary>
    /// Number of tools flagged as potentially destructive (no readOnlyHint, or destructiveHint=true).
    /// AI agents SHOULD require human confirmation before calling these.
    /// </summary>
    public int DestructiveToolCount { get; set; }

    /// <summary>
    /// Number of tools that could exfiltrate data (accept URI/URL/path parameters that could
    /// be manipulated to send data to external endpoints).
    /// </summary>
    public int DataExfiltrationRiskCount { get; set; }

    /// <summary>
    /// Number of tools/prompts with descriptions that could be used for prompt injection
    /// (contain instruction-like language that could override AI system prompts).
    /// </summary>
    public int PromptInjectionSurfaceCount { get; set; }

    /// <summary>
    /// Whether the server exposes tools without requiring human-in-the-loop confirmation.
    /// MCP spec: "there SHOULD always be a human in the loop with the ability to deny tool invocations."
    /// </summary>
    public bool HumanInLoopEnforced { get; set; }

    /// <summary>
    /// Average LLM-friendliness score across all tool error responses (0-100).
    /// Measures whether error messages help AI agents self-correct.
    /// Pro-LLM (70+): Errors mention param names, expected types, structured data.
    /// Anti-LLM (&lt;40): Generic errors causing hallucination/retry loops.
    /// </summary>
    public double LlmFriendlinessScore { get; set; } = -1;

    /// <summary>
    /// Detailed findings from AI boundary analysis.
    /// </summary>
    public List<AiBoundaryFinding> BoundaryFindings { get; set; } = new();

    // ─── Compliance Tier Results ─────────────────────────────────────

    /// <summary>
    /// Number of MUST requirements that passed.
    /// </summary>
    public int MustPassCount { get; set; }

    /// <summary>
    /// Number of MUST requirements that failed.
    /// Any MUST failure = non-compliant.
    /// </summary>
    public int MustFailCount { get; set; }

    /// <summary>
    /// Total MUST requirements checked.
    /// </summary>
    public int MustTotalCount { get; set; }

    /// <summary>
    /// Number of SHOULD requirements that passed.
    /// </summary>
    public int ShouldPassCount { get; set; }

    /// <summary>
    /// Number of SHOULD requirements that failed (penalty applied).
    /// </summary>
    public int ShouldFailCount { get; set; }

    /// <summary>
    /// Total SHOULD requirements checked.
    /// </summary>
    public int ShouldTotalCount { get; set; }

    /// <summary>
    /// Number of MAY features detected as supported (informational).
    /// </summary>
    public int MaySupported { get; set; }

    /// <summary>
    /// Total MAY features probed (informational).
    /// </summary>
    public int MayTotal { get; set; }

    /// <summary>
    /// Detailed MUST/SHOULD/MAY check results.
    /// </summary>
    public List<ComplianceTierCheck> TierChecks { get; set; } = new();
}

/// <summary>
/// Individual compliance tier check result.
/// </summary>
public class ComplianceTierCheck
{
    /// <summary>Tier: MUST, SHOULD, or MAY</summary>
    public string Tier { get; set; } = "MUST";

    /// <summary>The requirement description from McpComplianceTiers.</summary>
    public string Requirement { get; set; } = string.Empty;

    /// <summary>Pass or fail.</summary>
    public bool Passed { get; set; }

    /// <summary>Which component was checked (e.g., "initialize", "tools/call").</summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>Detail about what was observed.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Trust levels derived from multi-dimensional assessment.
/// The level is determined by weighted dimension scores and then capped by confirmed blockers.
/// </summary>
public enum McpTrustLevel
{
    Unknown = 0,

    /// <summary>L1: Critical failures found. Server is not safe for AI agent consumption.</summary>
    L1_Untrusted = 1,

    /// <summary>L2: Significant gaps. Usable with extreme caution and human oversight only.</summary>
    L2_Caution = 2,

    /// <summary>L3: Protocol compliant with known limitations. Acceptable for supervised AI use.</summary>
    L3_Acceptable = 3,

    /// <summary>L4: Meets enterprise requirements. Trusted for AI agent consumption with standard controls.</summary>
    L4_Trusted = 4,

    /// <summary>L5: Fully compliant, secure, AI-safe, and performant. Certified for production AI workloads.</summary>
    L5_CertifiedSecure = 5
}

/// <summary>
/// Individual finding from AI boundary-level analysis.
/// These go beyond MCP protocol to assess how AI agents interact with MCP servers.
/// </summary>
public class AiBoundaryFinding
{
    /// <summary>Category: Destructive, Exfiltration, PromptInjection, Hallucination, HumanInLoop</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The specific tool, resource, or prompt name involved.</summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>Severity: Critical, High, Medium, Low, Info</summary>
    public string Severity { get; set; } = "Medium";

    /// <summary>Human-readable description of the finding.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Recommended mitigation.</summary>
    public string Mitigation { get; set; } = string.Empty;
}
