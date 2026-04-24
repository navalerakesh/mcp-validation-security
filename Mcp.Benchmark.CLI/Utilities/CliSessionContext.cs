using System;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

/// <summary>
/// Represents session-scoped paths and metadata for a single CLI invocation.
/// </summary>
public sealed class CliSessionContext
{
    private readonly object _sync = new();

    public CliSessionContext(string sessionId)
        : this(sessionId, string.Empty, string.Empty, string.Empty, string.Empty, PersistenceMode.Ephemeral, RedactionLevel.Strict)
    {
    }

    public CliSessionContext(string sessionId, string sessionRoot, string stateDirectory, string logsDirectory, string logFilePath)
        : this(sessionId, sessionRoot, stateDirectory, logsDirectory, logFilePath, PersistenceMode.Session, RedactionLevel.Strict)
    {
    }

    public CliSessionContext(
        string sessionId,
        string sessionRoot,
        string stateDirectory,
        string logsDirectory,
        string logFilePath,
        PersistenceMode persistenceMode,
        RedactionLevel redactionLevel)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        SessionRoot = sessionRoot ?? throw new ArgumentNullException(nameof(sessionRoot));
        StateDirectory = stateDirectory ?? throw new ArgumentNullException(nameof(stateDirectory));
        LogsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
        LogFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));
        PersistenceMode = persistenceMode;
        RedactionLevel = redactionLevel;
    }

    public string SessionId { get; }

    public string SessionRoot { get; private set; }

    public string StateDirectory { get; private set; }

    public string LogsDirectory { get; private set; }

    public string LogFilePath { get; private set; }

    public PersistenceMode PersistenceMode { get; private set; }

    public RedactionLevel RedactionLevel { get; private set; }

    public bool CanPersistSessionArtifacts => !string.IsNullOrWhiteSpace(StateDirectory);

    public bool CanPersistLogs => !string.IsNullOrWhiteSpace(LogFilePath);

    public void ApplyExecutionPolicy(ExecutionPolicy? policy)
    {
        var effectivePolicy = policy ?? new ExecutionPolicy();

        lock (_sync)
        {
            PersistenceMode = effectivePolicy.PersistenceMode;
            RedactionLevel = effectivePolicy.RedactLevel;

            if (effectivePolicy.PersistenceMode != PersistenceMode.Session)
            {
                SessionRoot = string.Empty;
                StateDirectory = string.Empty;
                LogsDirectory = string.Empty;
                LogFilePath = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(SessionRoot))
            {
                return;
            }

            var sessionRoot = McpHostHelper.CreateSessionRoot(SessionId);
            var stateDirectory = Path.Combine(sessionRoot, "State");
            var logsDirectory = Path.Combine(sessionRoot, "Logs");

            Directory.CreateDirectory(stateDirectory);
            Directory.CreateDirectory(logsDirectory);

            SessionRoot = sessionRoot;
            StateDirectory = stateDirectory;
            LogsDirectory = logsDirectory;
            LogFilePath = Path.Combine(logsDirectory, $"mcpval-{SessionId}.log");
        }
    }
}
