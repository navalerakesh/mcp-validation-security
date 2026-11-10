using System.Reflection;
using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.CLI.Services;

internal static class WindowsSupervisorHost
{
    internal static async Task<int> RunSelfAsync()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(WindowsSupervisorProtocol.Hello(false), WindowsSupervisorProtocol.JsonOptions));
            return 64;
        }
        var executable = Environment.ProcessPath;
        if (executable is null) return 70;
        var prefix = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { Assembly.GetExecutingAssembly().Location }
            : Array.Empty<string>();
        try
        {
            using var streams = WindowsSupervisorConsoleStreams.Open();
            return await RunAsync(streams.Input, streams.Output,
                arguments => WindowsJobProcess.CreateSuspended(executable, prefix.Concat(arguments).ToArray()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return 70; }
    }

    internal static async Task<int> RunAsync(TextReader input, TextWriter output,
        Func<IReadOnlyList<string>, IWindowsJobProcess> createSuspended, TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        using var outputGate = new SemaphoreSlim(1, 1);
        using var stopIo = new CancellationTokenSource();
        IWindowsJobProcess? job = null;
        var launchAttempted = false;
        var cleanupConfirmed = false;
        var outputComplete = true;
        var exitCode = 70;
        var reason = "startup-error";
        var outputFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputTasks = Task.CompletedTask;
        Task<int>? parentControl = null;
        Task<IWindowsJobProcess>? creationTask = null;
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var admissionGate = new object();
        int? parentExit = null;
        var totalOutputBytes = 0;

        async Task EmitAsync(object frame, CancellationToken token)
        {
            var line = JsonSerializer.Serialize(frame, WindowsSupervisorProtocol.JsonOptions);
            await outputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await output.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            finally { outputGate.Release(); }
        }

        async Task PumpAsync(Stream source, string streamName)
        {
            var buffer = new byte[WindowsSupervisorProtocol.OutputChunkBytes];
            try
            {
                int count;
                while ((count = await source.ReadAsync(buffer, stopIo.Token).ConfigureAwait(false)) != 0)
                {
                    if (Interlocked.Add(ref totalOutputBytes, count) > WindowsSupervisorProtocol.MaximumOutputBytes)
                    {
                        outputFailure.TrySetResult();
                        return;
                    }
                    await EmitAsync(new { kind = "output", stream = streamName, data = Convert.ToBase64String(buffer, 0, count) }, stopIo.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                if (!stopIo.IsCancellationRequested) outputFailure.TrySetResult();
            }
        }

        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromMilliseconds(WindowsSupervisorProtocol.StartupTimeoutMilliseconds), clock);
            await EmitAsync(WindowsSupervisorProtocol.Hello(true), startup.Token).ConfigureAwait(false);
            var line = await WindowsSupervisorProtocol.ReadControlLineAsync(input, startup.Token).ConfigureAwait(false);
            var launch = WindowsSupervisorProtocol.ParseLaunch(line ?? throw new InvalidDataException("Supervisor launch was not supplied."));
            var started = clock.GetTimestamp();
            using var executionDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(launch.TimeoutMs), clock);
            parentControl = ReadParentControlAsync(input, resume, () => Volatile.Read(ref ready) == 1,
                code => { lock (admissionGate) parentExit ??= code; }, stopIo.Token);
            if (parentControl.IsCompleted)
            {
                exitCode = await parentControl.ConfigureAwait(false);
                reason = exitCode == 124 ? "cancelled-or-parent-closed" : "invalid-control";
            }
            else
            {
                launchAttempted = true;
                creationTask = Task.Run(() => createSuspended(launch.Arguments));
                job = await creationTask.WaitAsync(executionDeadline.Token).ConfigureAwait(false);
                if (parentControl.IsCompleted)
                {
                    exitCode = await parentControl.ConfigureAwait(false);
                    reason = exitCode == 124 ? "cancelled-or-parent-closed" : "invalid-control";
                }
                else if (executionDeadline.IsCancellationRequested || clock.GetElapsedTime(started).TotalMilliseconds >= launch.TimeoutMs)
                {
                    exitCode = 124;
                    reason = "deadline-before-resume";
                }
                else
                {
                    Volatile.Write(ref ready, 1);
                    await EmitAsync(new { kind = "ready", protocolVersion = WindowsSupervisorProtocol.Version }, executionDeadline.Token).ConfigureAwait(false);
                    await Task.WhenAny(resume.Task, parentControl).WaitAsync(executionDeadline.Token).ConfigureAwait(false);
                    if (parentControl.IsCompleted)
                    {
                        exitCode = await parentControl.ConfigureAwait(false);
                        reason = exitCode == 124 ? "cancelled-before-resume" : "invalid-control";
                    }
                    else
                    {
                        var admitted = false;
                        lock (admissionGate)
                        {
                            executionDeadline.Token.ThrowIfCancellationRequested();
                            var expired = clock.GetElapsedTime(started).TotalMilliseconds >= launch.TimeoutMs;
                            if (parentExit is int stopped)
                            {
                                exitCode = stopped;
                                reason = "parent-stopped-before-resume";
                            }
                            else if (expired) throw new OperationCanceledException(executionDeadline.Token);
                            else { job.Resume(); admitted = true; }
                        }
                        if (admitted)
                        {
                            outputTasks = Task.WhenAll(PumpAsync(job.StandardOutput, "stdout"), PumpAsync(job.StandardError, "stderr"));
                            while (true)
                            {
                                if (parentControl.IsCompleted)
                                {
                                    exitCode = await parentControl.ConfigureAwait(false);
                                    reason = exitCode == 124 ? "cancelled-or-parent-closed" : "invalid-control";
                                    break;
                                }
                                if (outputFailure.Task.IsCompleted || outputTasks.IsFaulted) { exitCode = 70; reason = "output-failure-or-budget"; break; }
                                if (job.TryGetExitCode(out exitCode)) { reason = "completed"; break; }
                                if (clock.GetElapsedTime(started).TotalMilliseconds >= launch.TimeoutMs)
                                {
                                    exitCode = 124;
                                    reason = "deadline";
                                    break;
                                }
                                await Task.Delay(TimeSpan.FromMilliseconds(WindowsSupervisorProtocol.PollMilliseconds), clock).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (job is not null)
        {
            exitCode = 124;
            reason = "deadline-before-completion";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            exitCode = launchAttempted ? 70 : 64;
            reason = launchAttempted ? "supervision-error" : "invalid-or-missing-launch";
        }
        finally
        {
            if (job is not null)
            {
                try
                {
                    var cleanupStarted = clock.GetTimestamp();
                    if (job.ActiveProcessCount > 0) job.Terminate(exitCode);
                    while (job.ActiveProcessCount > 0 && clock.GetElapsedTime(cleanupStarted).TotalMilliseconds < WindowsSupervisorProtocol.CleanupTimeoutMilliseconds)
                        await Task.Delay(TimeSpan.FromMilliseconds(WindowsSupervisorProtocol.PollMilliseconds), clock).ConfigureAwait(false);
                    cleanupConfirmed = job.ActiveProcessCount == 0;
                    if (cleanupConfirmed)
                    {
                        await outputTasks.WaitAsync(TimeSpan.FromMilliseconds(WindowsSupervisorProtocol.CleanupTimeoutMilliseconds), clock).ConfigureAwait(false);
                        outputComplete = !outputFailure.Task.IsCompleted;
                    }
                    else outputComplete = false;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    cleanupConfirmed = false;
                    outputComplete = false;
                }
                finally
                {
                    stopIo.Cancel();
                    try { await outputTasks.ConfigureAwait(false); }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { outputComplete = false; }
                    try { job.Dispose(); }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { cleanupConfirmed = false; }
                }
            }
            else
            {
                cleanupConfirmed = !launchAttempted;
                if (creationTask is not null) _ = CleanupLateCreationAsync(creationTask);
            }
            stopIo.Cancel();
            if (parentControl is not null) await parentControl.ConfigureAwait(false);
        }

        if (!cleanupConfirmed || !outputComplete) { exitCode = 70; reason = "cleanup-or-output-unconfirmed"; }
        try
        {
            using var finalWrite = new CancellationTokenSource(TimeSpan.FromMilliseconds(WindowsSupervisorProtocol.CleanupTimeoutMilliseconds), clock);
            await EmitAsync(new
            {
                kind = "exit",
                protocolVersion = WindowsSupervisorProtocol.Version,
                exitCode,
                cleanupConfirmed,
                activeProcesses = cleanupConfirmed ? (uint?)0 : null,
                outputComplete,
                reason
            }, finalWrite.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return 70; }
        return exitCode;
    }

    private static async Task CleanupLateCreationAsync(Task<IWindowsJobProcess> creation)
    {
        try
        {
            using var lateJob = await creation.ConfigureAwait(false);
            lateJob.Terminate(70);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine("Late supervisor creation cleanup remains unconfirmed.");
        }
    }

    private static async Task<int> ReadParentControlAsync(TextReader input, TaskCompletionSource resume, Func<bool> ready, Action<int> publish, CancellationToken token)
    {
        int Finish(int code) { publish(code); return code; }
        try
        {
            while (true)
            {
                var line = await WindowsSupervisorProtocol.ReadControlLineAsync(input, token).ConfigureAwait(false);
                if (line is null) return Finish(124);
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1 ||
                    !document.RootElement.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) return Finish(70);
                if (kind.ValueEquals("cancel")) return Finish(124);
                if (!kind.ValueEquals("resume") || !ready() || !resume.TrySetResult()) return Finish(70);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or
            OperationCanceledException or System.Text.DecoderFallbackException or ObjectDisposedException)
        {
            return Finish(70);
        }
    }
}
