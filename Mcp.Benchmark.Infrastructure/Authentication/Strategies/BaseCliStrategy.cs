using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Authentication.Strategies
{
    public abstract class BaseCliStrategy
    {
        protected readonly ILogger _logger;

        protected BaseCliStrategy(ILogger logger)
        {
            _logger = logger;
        }

        [Obsolete("Use the IReadOnlyList<string> overload so argument boundaries are explicit.")]
        protected Task<string?> RunCliCommandAsync(
            string executable,
            string arguments,
            CancellationToken cancellationToken,
            bool isInteractive = false)
        {
            return RunCliCommandAsync(
                executable,
                ParseArguments(arguments),
                cancellationToken,
                isInteractive);
        }

        protected async Task<string?> RunCliCommandAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool isInteractive = false)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = !isInteractive
            };

            foreach (var argument in arguments)
            {
                processStartInfo.ArgumentList.Add(argument);
            }

            if (isInteractive)
            {
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
                processStartInfo.RedirectStandardInput = false;
            }
            else
            {
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;
            }

            try
            {
                using var process = new Process { StartInfo = processStartInfo };
                if (!process.Start())
                {
                    _logger.LogDebug("Failed to start {Executable}", executable);
                    return null;
                }

                Task<BoundedTextReadResult>? outputTask = null;
                Task<BoundedTextReadResult>? errorTask = null;

                if (!isInteractive)
                {
                    void TerminateOnOverflow() => TryTerminateProcessTree(process);
                    outputTask = BoundedStreamReader.ReadUtf8Async(
                        process.StandardOutput.BaseStream,
                        ExecutionPolicyDefaults.DefaultMaxSubprocessOutputBytes,
                        BoundedStreamRetention.First,
                        TerminateOnOverflow,
                        cancellationToken);
                    errorTask = BoundedStreamReader.ReadUtf8Async(
                        process.StandardError.BaseStream,
                        ExecutionPolicyDefaults.DefaultMaxSubprocessOutputBytes,
                        BoundedStreamRetention.First,
                        TerminateOnOverflow,
                        cancellationToken);
                }

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TryTerminateProcessTree(process);
                    await ObserveReaderCancellationAsync(outputTask, errorTask, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                BoundedTextReadResult? output = null;
                BoundedTextReadResult? error = null;
                if (!isInteractive)
                {
                    output = await outputTask!.ConfigureAwait(false);
                    error = await errorTask!.ConfigureAwait(false);
                    if (output.IsTruncated || error.IsTruncated)
                    {
                        _logger.LogWarning(
                            "{Executable} output exceeded the subprocess capture limit. stdout bytes={StdoutBytes}; stderr bytes={StderrBytes}",
                            executable,
                            output.TotalBytes,
                            error.TotalBytes);
                        return null;
                    }
                }

                if (process.ExitCode == 0)
                {
                    return isInteractive ? "success" : output!.Text;
                }

                if (!isInteractive)
                {
                    _logger.LogDebug(
                        "{Executable} command failed with exit code {ExitCode}; stderr bytes={StderrBytes}",
                        executable,
                        process.ExitCode,
                        error!.TotalBytes);
                }

                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to run {Executable}", executable);
                return null;
            }
        }

        private async Task ObserveReaderCancellationAsync(
            Task<BoundedTextReadResult>? outputTask,
            Task<BoundedTextReadResult>? errorTask,
            CancellationToken cancellationToken)
        {
            if (outputTask == null || errorTask == null)
            {
                return;
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Subprocess output readers observed caller cancellation");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
            {
                _logger.LogDebug(ex, "Subprocess output streams closed during cancellation cleanup");
            }
        }

        private static void TryTerminateProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
            }
        }

        private static IReadOnlyList<string> ParseArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return Array.Empty<string>();
            }

            var parsed = new List<string>();
            var current = new System.Text.StringBuilder();
            char? quote = null;
            var escaped = false;

            foreach (var character in arguments)
            {
                if (escaped)
                {
                    current.Append(character);
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character is '\'' or '"')
                {
                    if (quote == character)
                    {
                        quote = null;
                    }
                    else if (quote == null)
                    {
                        quote = character;
                    }
                    else
                    {
                        current.Append(character);
                    }
                    continue;
                }

                if (char.IsWhiteSpace(character) && quote == null)
                {
                    if (current.Length > 0)
                    {
                        parsed.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(character);
            }

            if (escaped)
            {
                current.Append('\\');
            }

            if (quote != null)
            {
                throw new ArgumentException("Subprocess argument string contains an unterminated quote.", nameof(arguments));
            }

            if (current.Length > 0)
            {
                parsed.Add(current.ToString());
            }

            return parsed;
        }
    }
}
