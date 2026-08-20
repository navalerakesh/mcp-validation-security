namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ReportTerminology
{
    public static IReadOnlyList<ReportTerm> Entries { get; } =
    [
        new("MCP", "Model Context Protocol, the communication protocol evaluated by this report."),
        new("MCP specification profile", "The dated MCP rules and schema version selected for validation."),
        new("Priority 1-4", "Remediation dependency order: Priority 1 should be addressed first and Priority 4 later. Priority is not severity."),
        new("Trust levels L1-L5", "A descriptive benchmark scale from L1 (untrusted) to L5 (high assurance). It is not a certification and does not override the deterministic verdict."),
        new("Deterministic verdict", "The authoritative gate derived from rule outcomes and evidence coverage; benchmark scores are descriptive only."),
        new("Authority labels", "Specification means a protocol requirement; Guideline means recommended practice; Heuristic means deterministic advisory analysis; Operational means runtime or infrastructure evidence."),
        new("JSON-RPC / HTTP", "JSON-RPC is JavaScript Object Notation (JSON) for remote procedure call (RPC) messages used by MCP. HTTP is the Hypertext Transfer Protocol used to transport messages in this run."),
        new("RFC 2119: MUST / SHOULD / MAY", "Requirement strength: MUST is required, SHOULD is recommended unless a justified exception exists, and MAY is optional."),
        new("P50 / P95 / P99", "Latency percentiles: 50%, 95%, or 99% of measured requests completed at or below the reported value."),
        new("Units and time", "ms means milliseconds, req/s means requests per second, and UTC means Coordinated Universal Time."),
        new("AI / LLM / CLI / UX", "Artificial intelligence; large language model; command-line interface; and user experience."),
        new("VS Code", "Visual Studio Code, a supported client profile named in compatibility results."),
        new("Identifiers: ID / UUID", "ID means identifier. UUID means Universally Unique Identifier. Validation and rule identifiers are stable machine references, not scores.")
    ];
}

internal sealed record ReportTerm(string Term, string Meaning);