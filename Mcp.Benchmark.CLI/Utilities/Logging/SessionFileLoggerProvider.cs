using System;
using System.IO;
using System.Text;
using Mcp.Benchmark.CLI.Utilities;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.CLI.Utilities.Logging;

internal sealed class SessionFileLoggerProvider : ILoggerProvider
{
    private readonly CliSessionContext _sessionContext;
    private readonly LogLevel _minimumLevel;
    private readonly object _lock = new();
    private bool _disposed;

    public SessionFileLoggerProvider(CliSessionContext sessionContext, LogLevel minimumLevel = LogLevel.Information)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionFileLoggerProvider));
        }

        return new SessionFileLogger(categoryName, _sessionContext, _minimumLevel, _lock);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private sealed class SessionFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly CliSessionContext _sessionContext;
        private readonly LogLevel _minimumLevel;
        private readonly object _lock;

        public SessionFileLogger(string categoryName, CliSessionContext sessionContext, LogLevel minimumLevel, object sharedLock)
        {
            _categoryName = categoryName;
            _sessionContext = sessionContext;
            _minimumLevel = minimumLevel;
            _lock = sharedLock;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (!_sessionContext.CanPersistLogs)
            {
                return;
            }

            var message = SessionLogRedactor.Redact(formatter(state, exception), _sessionContext.RedactionLevel);
            var logFilePath = _sessionContext.LogFilePath;
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("o"))
                .Append(' ')
                .Append('[')
                .Append(logLevel)
                .Append(']')
                .Append(' ')
                .Append(_categoryName)
                .Append(':')
                .Append(' ')
                .Append(message);

            if (exception != null)
            {
                builder.AppendLine()
                       .Append(exception);
            }

            lock (_lock)
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(logFilePath, builder.AppendLine().ToString());
            }
        }
    }
}
