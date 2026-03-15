using System;

namespace Mcp.Benchmark.CLI.Utilities;

/// <summary>
/// Represents session-scoped paths and metadata for a single CLI invocation.
/// </summary>
public sealed class CliSessionContext
{
    public CliSessionContext(string sessionId, string sessionRoot, string stateDirectory, string logsDirectory, string logFilePath)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        SessionRoot = sessionRoot ?? throw new ArgumentNullException(nameof(sessionRoot));
        StateDirectory = stateDirectory ?? throw new ArgumentNullException(nameof(stateDirectory));
        LogsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
        LogFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));
    }

    public string SessionId { get; }

    public string SessionRoot { get; }

    public string StateDirectory { get; }

    public string LogsDirectory { get; }

    public string LogFilePath { get; }
}
