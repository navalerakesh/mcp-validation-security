using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Abstractions;

public interface IAttackVector
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken);
}

public class AttackResult
{
    public string VectorId { get; set; } = "";
    public string VectorName { get; set; } = "";
    public bool IsBlocked { get; set; }
    public AttackSimulationOutcome Outcome { get; set; } = AttackSimulationOutcome.Blocked;
    public string Severity { get; set; } = "High"; // Critical, High, Medium, Low
    public string Analysis { get; set; } = "";
    public string Evidence { get; set; } = "";
    public List<ProbeContext> ProbeContexts { get; set; } = new();
}
