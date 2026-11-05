namespace Mcp.Benchmark.Core.Exceptions;

public sealed class OfficialConformanceEvidenceException : McpValidationException
{
    public OfficialConformanceEvidenceException(string message, Exception? innerException = null)
        : base(message, "MCP_OFFICIAL_CONFORMANCE_EVIDENCE_INVALID", innerException)
    {
    }
}