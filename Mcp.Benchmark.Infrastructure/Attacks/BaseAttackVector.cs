using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Infrastructure.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Services;

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

    protected AttackResult CreateResult(
        bool isBlocked,
        string analysis,
        string evidence,
        string severity = "High",
        AttackSimulationOutcome? outcome = null,
        IEnumerable<ProbeContext>? probeContexts = null)
    {
        return new AttackResult
        {
            VectorId = Id,
            VectorName = Name,
            IsBlocked = isBlocked,
            Outcome = outcome ?? (isBlocked ? AttackSimulationOutcome.Blocked : AttackSimulationOutcome.Detected),
            Severity = severity,
            Analysis = analysis,
            Evidence = evidence,
            ProbeContexts = probeContexts?.Where(context => context != null).ToList() ?? new List<ProbeContext>()
        };
    }

    protected AttackResult CreateSkippedResult(string analysis, string evidence, string severity = "Low", IEnumerable<ProbeContext>? probeContexts = null)
        => CreateResult(true, analysis, evidence, severity, AttackSimulationOutcome.Skipped, probeContexts);

    protected static List<ProbeContext> CollectProbeContexts(params ProbeContext?[] probeContexts)
        => probeContexts.Where(context => context != null).Cast<ProbeContext>().ToList();
}
