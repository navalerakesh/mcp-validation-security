using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Attacks;

public abstract class BaseAttackVector : IAttackVector
{
    protected readonly ILogger _logger;

    protected BaseAttackVector(ILogger logger)
    {
        _logger = logger;
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }

    public abstract Task<AttackResult> ExecuteAsync(McpServerConfig serverConfig, IMcpHttpClient client, CancellationToken cancellationToken);

    protected AttackResult CreateResult(bool isBlocked, string analysis, string evidence, string severity = "High")
    {
        return new AttackResult
        {
            VectorId = Id,
            VectorName = Name,
            IsBlocked = isBlocked,
            Severity = severity,
            Analysis = analysis,
            Evidence = evidence
        };
    }
}
