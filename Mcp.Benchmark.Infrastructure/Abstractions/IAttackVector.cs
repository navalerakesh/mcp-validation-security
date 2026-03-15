using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;

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
    public string Severity { get; set; } = "High"; // Critical, High, Medium, Low
    public string Analysis { get; set; } = "";
    public string Evidence { get; set; } = "";
}
