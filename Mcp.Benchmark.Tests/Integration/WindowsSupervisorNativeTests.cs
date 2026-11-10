using System.Diagnostics;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
using Microsoft.Win32.SafeHandles;

namespace Mcp.Benchmark.Tests.Integration;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Requires native Windows job objects; portable compilation is not Windows qualification.";
    }
}

[Trait("Category", "WindowsNative")]
public sealed class WindowsSupervisorNativeTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    [WindowsFact]
    public async Task ConsolePipes_CancelPendingRead_AndPreserveStrictUtf8AndOriginalHandles()
    {
        using var input = await ConsolePipePair.CreateAsync();
        using var output = await ConsolePipePair.CreateAsync();
        using var streams = WindowsSupervisorConsoleStreams.Open(input.Client, output.Client);
        using var stop = new CancellationTokenSource();
        var buffer = new char[16];
        var pending = streams.Input.ReadAsync(buffer, stop.Token).AsTask();
        Assert.False(pending.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(WaitLimit));

        var line = streams.Input.ReadLineAsync().WaitAsync(WaitLimit);
        await input.Server.WriteAsync(new byte[] { 0xce }).AsTask().WaitAsync(WaitLimit);
        Assert.False(line.IsCompleted);
        await input.Server.WriteAsync(new byte[] { 0xbb, 10 }).AsTask().WaitAsync(WaitLimit);
        Assert.Equal("\u03bb", await line);
        await streams.Output.WriteLineAsync("\u03bb").WaitAsync(WaitLimit);
        await streams.Output.FlushAsync().WaitAsync(WaitLimit);
        var encoded = new byte[3];
        await output.Server.ReadExactlyAsync(encoded).AsTask().WaitAsync(WaitLimit);
        Assert.Equal(new byte[] { 0xce, 0xbb, 10 }, encoded);

        await Assert.ThrowsAsync<EncoderFallbackException>(() => streams.Output.WriteAsync("\ud800"));
        await input.Server.WriteAsync(new byte[] { 0xff, 10 }).AsTask().WaitAsync(WaitLimit);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => streams.Input.ReadLineAsync().WaitAsync(WaitLimit));
        streams.Dispose();
        streams.Dispose();
        Assert.True(NativePipeMethods.GetHandleInformation(input.Client, out _));
        Assert.True(NativePipeMethods.GetHandleInformation(output.Client, out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => streams.Input.ReadAsync(buffer).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => streams.Output.WriteAsync("closed"));
    }

    [WindowsFact]
    public async Task ConsolePipes_CancelBackpressuredWrite_WithoutClosingInput()
    {
        using var input = await ConsolePipePair.CreateAsync();
        using var output = await ConsolePipePair.CreateAsync();
        using var streams = WindowsSupervisorConsoleStreams.Open(input.Client, output.Client);
        using var stop = new CancellationTokenSource();
        var write = streams.Output.WriteAsync(new string('x', 1024 * 1024).AsMemory(), stop.Token);
        Assert.False(write.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write.WaitAsync(WaitLimit));
        await input.Server.WriteAsync("ok\n"u8.ToArray()).AsTask().WaitAsync(WaitLimit);
        Assert.Equal("ok", await streams.Input.ReadLineAsync().WaitAsync(WaitLimit));
    }

    [WindowsFact]
    public async Task ConsolePipes_DisposeCancelsAndDrainsBothDirectionsBeforeReturning()
    {
        using var input = await ConsolePipePair.CreateAsync();
        using var output = await ConsolePipePair.CreateAsync();
        using var streams = WindowsSupervisorConsoleStreams.Open(input.Client, output.Client);
        var read = streams.Input.ReadAsync(new char[1]).AsTask();
        var write = streams.Output.WriteAsync(new string('x', 1024 * 1024));
        var queuedWrite = streams.Output.WriteLineAsync("queued");
        var flush = streams.Output.FlushAsync();
        Assert.False(read.IsCompleted);
        Assert.False(write.IsCompleted);
        Assert.False(queuedWrite.IsCompleted);
        Assert.False(flush.IsCompleted);
        await Task.Run(streams.Dispose).WaitAsync(WaitLimit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(WaitLimit));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write.WaitAsync(WaitLimit));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedWrite.WaitAsync(WaitLimit));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush.WaitAsync(WaitLimit));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => streams.Output.FlushAsync());
        Assert.True(NativePipeMethods.GetHandleInformation(input.Client, out _));
        Assert.True(NativePipeMethods.GetHandleInformation(output.Client, out _));
    }

    [WindowsFact]
    public async Task ConsolePipes_RejectSynchronousInvalidAndNonPipeHandlesWithoutClosingSources()
    {
        using var input = await ConsolePipePair.CreateAsync();
        using var output = await ConsolePipePair.CreateAsync();
        using var synchronous = await ConsolePipePair.CreateAsync(asynchronous: false);
        using var workspace = new Workspace();
        using var file = File.OpenHandle(workspace.File("not-a-pipe"), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, FileOptions.Asynchronous);
        using var invalid = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        Assert.Throws<IOException>(() => WindowsSupervisorConsoleStreams.Open(synchronous.Client, output.Client));
        Assert.Throws<IOException>(() => WindowsSupervisorConsoleStreams.Open(input.Client, synchronous.Client));
        Assert.Throws<IOException>(() => WindowsSupervisorConsoleStreams.Open(file, output.Client));
        Assert.Throws<IOException>(() => WindowsSupervisorConsoleStreams.Open(invalid, output.Client));
        Assert.True(NativePipeMethods.GetHandleInformation(input.Client, out _));
        Assert.True(NativePipeMethods.GetHandleInformation(output.Client, out _));
        Assert.True(NativePipeMethods.GetHandleInformation(synchronous.Client, out _));
    }

    [WindowsFact]
    public async Task WorkerPipes_AreNoninheritableAsyncReaders_AndCancellationDoesNotRequireWorkerExit()
    {
        using var workspace = new Workspace();
        using var job = WindowsJobProcess.CreateSuspended(NodePath(),
            [Fixture("windows-supervisor-tree.cjs"), "echo", workspace.Root]);
        foreach (var stream in new[] { job.StandardOutput, job.StandardError })
        {
            var pipe = Assert.IsType<NamedPipeServerStream>(stream);
            Assert.True(pipe.IsAsync);
            using var handle = new SafeFileHandle(pipe.SafePipeHandle.DangerousGetHandle(), ownsHandle: false);
            Assert.True(NativePipeMethods.GetHandleInformation(handle, out var flags));
            Assert.Equal(0u, flags & 1);
            using var stop = new CancellationTokenSource();
            var read = stream.ReadAsync(new byte[1], stop.Token).AsTask();
            Assert.False(read.IsCompleted);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(WaitLimit));
            Assert.Equal(1u, job.ActiveProcessCount);
        }
        job.Terminate(124);
        await AssertEmptyAsync(job);
    }

    [WindowsFact]
    public async Task WorkerStdin_ReceivesEofWhileOutputPipesRemainUsable()
    {
        using var workspace = new Workspace();
        using var job = WindowsJobProcess.CreateSuspended(NodePath(),
            [Fixture("windows-supervisor-tree.cjs"), "stdin-eof", workspace.Root]);
        using var output = new StreamReader(job.StandardOutput);
        using var error = new StreamReader(job.StandardError);
        var stdout = output.ReadToEndAsync();
        var stderr = error.ReadToEndAsync();
        job.Resume();
        File.WriteAllText(workspace.File("release"), "release");
        Assert.Equal("stdin-eof", (await stdout.WaitAsync(WaitLimit)).Trim());
        Assert.Equal("fixture-stderr", (await stderr.WaitAsync(WaitLimit)).Trim());
        await AssertEmptyAsync(job);
        Assert.True(job.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
    }

    [WindowsFact]
    public async Task SuspendedWorker_ProducesNoEvidenceUntilResume_AndPreservesArguments()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        string[] arguments = ["", "plain", "two words", "\u65e5\u672c\u8a9e-\u03bb-\ud83d\ude80", "a\"b", "\\", "tail\\", "two\\\\", "slash\\\"quote"];
        using var job = WindowsJobProcess.CreateSuspended(NodePath(),
            [Fixture("windows-supervisor-tree.cjs"), "echo", workspace.Root, .. arguments]);
        using var output = new StreamReader(job.StandardOutput);
        using var error = new StreamReader(job.StandardError);
        var stdout = output.ReadToEndAsync();
        var stderr = error.ReadToEndAsync();

        await Task.Delay(350);
        Assert.False(File.Exists(workspace.File("leader.json")));
        Assert.False(job.TryGetExitCode(out _));
        Assert.Equal(1u, job.ActiveProcessCount);

        job.Resume();
        var leader = await CaptureAsync(workspace, "leader");
        tracked.Items.Add(leader);
        File.WriteAllText(workspace.File("release"), "release");
        await AssertEmptyAsync(job, tracked);
        Assert.True(job.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.Equal(arguments, JsonSerializer.Deserialize<string[]>(await stdout.WaitAsync(WaitLimit)));
        Assert.Equal("fixture-stderr", (await stderr.WaitAsync(WaitLimit)).Trim());
    }

    [WindowsFact]
    public async Task ForcedTermination_KillsEveryGenerationWithClosedStdio()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        using var job = StartTree(workspace);
        await tracked.CaptureTreeAsync(workspace);
        Assert.Equal(4u, job.ActiveProcessCount);
        job.Terminate(124);
        await AssertEmptyAsync(job, tracked);
        Assert.True(job.TryGetExitCode(out var exitCode));
        Assert.Equal(124, exitCode);
    }

    [WindowsFact]
    public async Task LeaderExitAndPipeEof_DoNotImplyWholeJobCompletion()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        using var job = StartTree(workspace);
        using var output = new StreamReader(job.StandardOutput);
        using var error = new StreamReader(job.StandardError);
        var stdout = output.ReadToEndAsync();
        var stderr = error.ReadToEndAsync();
        await tracked.CaptureTreeAsync(workspace);
        File.WriteAllText(workspace.File("leader-exit"), "exit");
        await WaitUntilAsync(() => job.TryGetExitCode(out _), "leader exit");
        Assert.True(job.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.Empty(await stdout.WaitAsync(WaitLimit));
        Assert.Empty(await stderr.WaitAsync(WaitLimit));
        Assert.True(tracked.Items[0].WaitForExit(0));
        tracked.Items[0].Dispose();
        tracked.Items.RemoveAt(0);
        for (var observation = 0; observation < 4; observation++)
        {
            await Task.Delay(100);
            Assert.Equal(3u, job.ActiveProcessCount);
            Assert.All(tracked.Items, process => Assert.False(process.HasExited));
        }
        job.Terminate(125);
        await AssertEmptyAsync(job, tracked);
    }

    [WindowsFact]
    public async Task Dispose_KillsWholeJobWithoutExplicitTermination()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        using var job = StartTree(workspace);
        await tracked.CaptureTreeAsync(workspace);
        Assert.Equal(4u, job.ActiveProcessCount);
        job.Dispose();
        await tracked.AssertExitedAndReleaseAsync();
        Assert.Throws<ObjectDisposedException>(() => job.ActiveProcessCount);
    }

    [WindowsFact]
    public async Task PrivateJobs_TerminationDoesNotAffectOtherOwners()
    {
        using var firstWorkspace = new Workspace();
        using var secondWorkspace = new Workspace();
        using var firstProcesses = new CapturedProcesses();
        using var secondProcesses = new CapturedProcesses();
        using var first = StartTree(firstWorkspace);
        using var second = StartTree(secondWorkspace);
        await firstProcesses.CaptureTreeAsync(firstWorkspace);
        await secondProcesses.CaptureTreeAsync(secondWorkspace);
        first.Terminate(124);
        await AssertEmptyAsync(first, firstProcesses);
        for (var observation = 0; observation < 4; observation++)
        {
            await Task.Delay(100);
            Assert.Equal(4u, second.ActiveProcessCount);
            Assert.All(secondProcesses.Items, process => Assert.False(process.HasExited));
        }
        second.Terminate(125);
        await AssertEmptyAsync(second, secondProcesses);
    }

    [WindowsFact]
    public async Task Host_Version_CompletesWithDecodedOutputAndZeroProcessReceipt()
    {
        using var workspace = new Workspace();
        await using var host = await HostSession.StartAsync(workspace);
        await AssertHelloAsync(host.Controller.StandardOutput);
        var frames = ReadFramesAsync(host.Controller.StandardOutput, host.ResumeAsync);
        await host.LaunchAsync(["--version"], 20000);
        var result = await frames.WaitAsync(WaitLimit);
        AssertReceipt(result, 0, "completed");
        Assert.Matches(@"\d+\.\d+\.\d+", DecodeOutput(result, "stdout"));
        await host.AssertExitAsync(0);
    }

    [WindowsFact]
    public Task Host_WorkerCompletion_CleansLivingDescendants() => RunHostTreeAsync("complete");

    [WindowsFact]
    public Task Host_Cancellation_ConfirmsWholeJobCleanup() => RunHostTreeAsync("cancel");

    [WindowsFact]
    public Task Host_ControlEof_ConfirmsWholeJobCleanup() => RunHostTreeAsync("eof");

    [WindowsFact]
    public Task Host_Deadline_ConfirmsWholeJobCleanup() => RunHostTreeAsync("deadline");

    [WindowsFact]
    public Task Host_AbruptLoss_KillsWorkerAndEveryDescendantWithoutReceipt() => RunHostTreeAsync("abrupt");

    [WindowsFact]
    public async Task Host_MissingLaunch_CancelsRealPendingInputWithParentStillOpen()
    {
        using var workspace = new Workspace();
        await using var host = await HostSession.StartAsync(workspace);
        await AssertHelloAsync(host.Controller.StandardOutput);
        var frames = await ReadFramesAsync(host.Controller.StandardOutput).WaitAsync(WaitLimit);
        AssertReceipt(frames, 64, "invalid-or-missing-launch");
        await host.AssertExitAsync(64);
    }

    [WindowsFact]
    public Task Host_ControlEofBeforeResume_ProducesNoWorkerOutput() => RunHostWithoutResumeAsync("eof");

    [WindowsFact]
    public Task Host_CancelBeforeResume_ProducesNoWorkerOutput() => RunHostWithoutResumeAsync("cancel");

    [WindowsFact]
    public Task Host_InvalidResume_ProducesNoWorkerOutput() => RunHostWithoutResumeAsync("invalid");

    private static async Task RunHostWithoutResumeAsync(string control)
    {
        using var workspace = new Workspace();
        await using var host = await HostSession.StartAsync(workspace);
        await AssertHelloAsync(host.Controller.StandardOutput);
        var frames = ReadFramesAsync(host.Controller.StandardOutput, async () =>
        {
            if (control == "eof") host.Controller.StandardInput.Close();
            else
            {
                await host.Controller.StandardInput.WriteLineAsync(control == "cancel"
                    ? "{\"kind\":\"cancel\"}" : "{\"kind\":\"resume\",\"extra\":true}").WaitAsync(WaitLimit);
                await host.Controller.StandardInput.FlushAsync().WaitAsync(WaitLimit);
            }
        });
        await host.LaunchAsync(["--version"], 20000);
        var result = await frames.WaitAsync(WaitLimit);
        Assert.Single(result, frame => frame.GetProperty("kind").GetString() == "ready");
        Assert.DoesNotContain(result, frame => frame.GetProperty("kind").GetString() == "output");
        var exitCode = control == "invalid" ? 70 : 124;
        AssertReceipt(result, exitCode, control == "invalid" ? "invalid-control" : "cancelled-before-resume");
        await host.AssertExitAsync(exitCode);
    }

    [WindowsFact]
    public async Task NestedJob_ActualHostCanCreateAndCleanItsInnerJob()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        using var outer = WindowsJobProcess.CreateSuspended(NodePath(),
            [Fixture("windows-supervisor-parent.cjs"), CliPath(), workspace.Root, "nested"]);
        using var output = new StreamReader(outer.StandardOutput);
        using var error = new StreamReader(outer.StandardError);
        var stderr = error.ReadToEndAsync();
        outer.Resume();
        tracked.Items.Add(await CaptureAsync(workspace, "leader"));
        tracked.Items.Add(await CaptureAsync(workspace, "host"));
        await AssertHelloAsync(output);
        Assert.Equal(2u, outer.ActiveProcessCount);
        var frames = ReadFramesAsync(output);
        File.WriteAllText(workspace.File("release"), "release");
        await tracked.AssertExitedAndReleaseAsync();
        var result = await frames.WaitAsync(WaitLimit);
        AssertReceipt(result, 0, "completed");
        Assert.Matches(@"\d+\.\d+\.\d+", DecodeOutput(result, "stdout"));
        await AssertEmptyAsync(outer, tracked);
        Assert.True(outer.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.Empty(await stderr.WaitAsync(WaitLimit));
    }

    [WindowsFact]
    public async Task BreakawayCreation_IsDeniedByThePrivateJob()
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(powershell), "Windows PowerShell is required for the fixed trusted breakaway fixture.");
        using var job = WindowsJobProcess.CreateSuspended(powershell,
            ["-NoProfile", "-NonInteractive", "-File", Fixture("windows-supervisor-breakaway.ps1"), "-Root", workspace.Root]);
        using var output = new StreamReader(job.StandardOutput);
        using var error = new StreamReader(job.StandardError);
        var stdout = output.ReadToEndAsync();
        var stderr = error.ReadToEndAsync();
        job.Resume();
        tracked.Items.Add(await CaptureAsync(workspace, "leader"));
        File.WriteAllText(workspace.File("release"), "release");
        await AssertEmptyAsync(job, tracked);
        Assert.True(job.TryGetExitCode(out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.Empty(await stderr.WaitAsync(WaitLimit));
        using var receipt = JsonDocument.Parse(await stdout.WaitAsync(WaitLimit));
        Assert.False(receipt.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal(5, receipt.RootElement.GetProperty("error").GetInt32());
    }

    private static async Task RunHostTreeAsync(string termination)
    {
        using var workspace = new Workspace();
        using var tracked = new CapturedProcesses();
        await using var host = await HostSession.StartAsync(workspace);
        await AssertHelloAsync(host.Controller.StandardOutput);
        var frames = ReadFramesAsync(host.Controller.StandardOutput, host.ResumeAsync);
        var methods = workspace.File("methods.jsonl");
        var serverArguments = new List<string>
        {
            NodePath(), Fixture("windows-supervisor-tree.cjs"), "mcp", workspace.Root,
            "--audit-methods", methods, "--audit-processes", workspace.File("processes.jsonl")
        };
        if (termination != "complete") serverArguments.AddRange(["--hold-method", "tools/list"]);
        Assert.All(serverArguments, argument => Assert.DoesNotContain('"', argument));
        var server = string.Join(" ", serverArguments.Select(argument => "\"" + argument + "\""));
        string[] arguments = termination == "complete"
            ? ["health-check", "--server", server, "--protocol-era", "legacy", "--timeout", "60"]
            : ["validate", "--server", server, "--protocol-era", "legacy", "--mcpspec", "2025-11-25",
                "--assurance", "inventory", "--mode", "safe", "--timeout", "60", "--output", workspace.File("reports")];
        await host.LaunchAsync(arguments, termination == "deadline" ? 20000 : 60000);
        await tracked.CaptureTreeAsync(workspace);
        using (var ready = JsonDocument.Parse(File.ReadAllText(workspace.File("leader.json"))))
        {
            var worker = Capture(ready.RootElement.GetProperty("parentPid").GetInt32());
            tracked.Items.Add(worker);
            Assert.NotEqual(host.Supervisor.Id, worker.Id);
            Assert.Equal(CliPath(), worker.MainModule!.FileName, ignoreCase: true);
        }
        Assert.Equal(5, tracked.Items.Select(process => process.Id).Distinct().Count());
        Assert.All(tracked.Items, process => Assert.False(process.HasExited));
        File.WriteAllText(workspace.File("release"), "release");
        if (termination != "complete")
        {
            await WaitUntilAsync(() => HasHeldRequest(methods), "actual CLI tools/list request held by fixture");
            Assert.All(tracked.Items, process => Assert.False(process.HasExited));
        }
        switch (termination)
        {
            case "cancel":
                await host.Controller.StandardInput.WriteLineAsync("{\"kind\":\"cancel\"}").WaitAsync(WaitLimit);
                await host.Controller.StandardInput.FlushAsync().WaitAsync(WaitLimit);
                break;
            case "eof":
                host.Controller.StandardInput.Close();
                break;
            case "abrupt":
                host.Supervisor.Kill();
                break;
        }
        await tracked.AssertExitedAndReleaseAsync();
        var result = await frames.WaitAsync(WaitLimit);
        if (termination == "abrupt")
        {
            Assert.DoesNotContain(result, frame => frame.GetProperty("kind").GetString() == "exit");
            await host.Supervisor.WaitForExitAsync().WaitAsync(WaitLimit);
            await host.Controller.WaitForExitAsync().WaitAsync(WaitLimit);
        }
        else
        {
            var exitCode = termination == "complete" ? 0 : 124;
            var reason = termination switch
            {
                "complete" => "completed",
                "deadline" => "deadline",
                _ => "cancelled-or-parent-closed"
            };
            AssertReceipt(result, exitCode, reason);
            await host.AssertExitAsync(exitCode);
        }
    }

    private static bool HasHeldRequest(string methods)
    {
        if (!File.Exists(methods)) return false;
        using var input = new FileStream(methods, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.InRange(input.Length, 0, 64 * 1024);
        using var reader = new StreamReader(input);
        var text = reader.ReadToEnd();
        var delimiter = text.LastIndexOf('\n');
        if (delimiter < 0) return false;
        foreach (var line in text[..delimiter].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("method").GetString() == "tools/list") return true;
        }
        return false;
    }

    private static string CliPath()
    {
        var executable = Environment.GetEnvironmentVariable("MCPVAL_WINDOWS_TEST_CLI");
        Assert.True(!string.IsNullOrWhiteSpace(executable) && Path.IsPathFullyQualified(executable) && File.Exists(executable),
            "Set MCPVAL_WINDOWS_TEST_CLI to the absolute freshly built Windows CLI apphost. Missing prerequisites are failures.");
        Assert.Equal(".exe", Path.GetExtension(executable), ignoreCase: true);
        return Path.GetFullPath(executable!);
    }

    private static async Task AssertHelloAsync(StreamReader output)
    {
        using var document = JsonDocument.Parse(await ReadFrameLineAsync(output) ?? throw new InvalidDataException("Missing hello."));
        var hello = document.RootElement;
        Assert.Equal("hello", hello.GetProperty("kind").GetString());
        Assert.Equal(WindowsSupervisorProtocol.Name, hello.GetProperty("protocol").GetString());
        Assert.Equal(WindowsSupervisorProtocol.Version, hello.GetProperty("protocolVersion").GetInt32());
        Assert.True(hello.GetProperty("supported").GetBoolean());
    }

    private static async Task<string?> ReadFrameLineAsync(StreamReader output)
    {
        using var deadline = new CancellationTokenSource(WaitLimit);
        return await WindowsSupervisorProtocol.ReadControlLineAsync(output, deadline.Token).WaitAsync(WaitLimit);
    }

    private static async Task<List<JsonElement>> ReadFramesAsync(StreamReader output, Func<Task>? onReady = null)
    {
        var frames = new List<JsonElement>();
        var totalBytes = 0;
        var readyReceived = false;
        var terminalReceived = false;
        while (await ReadFrameLineAsync(output) is { } line)
        {
            Assert.False(terminalReceived, "Frame after terminal receipt.");
            Assert.True(frames.Count < 4096, "Supervisor frame budget exceeded.");
            using var document = JsonDocument.Parse(line);
            var frame = document.RootElement;
            var kind = frame.GetProperty("kind").GetString();
            Assert.Contains(kind, new[] { "ready", "output", "exit" });
            if (kind == "ready")
            {
                Assert.False(readyReceived, "Duplicate ready frame.");
                Assert.Equal(new[] { "kind", "protocolVersion" }, frame.EnumerateObject().Select(property => property.Name).Order().ToArray());
                Assert.Equal(WindowsSupervisorProtocol.Version, frame.GetProperty("protocolVersion").GetInt32());
                readyReceived = true;
                if (onReady is not null) await onReady();
            }
            if (kind == "output")
            {
                Assert.True(readyReceived, "Output before ready.");
                Assert.Contains(frame.GetProperty("stream").GetString(), new[] { "stdout", "stderr" });
                var data = Convert.FromBase64String(frame.GetProperty("data").GetString()!);
                Assert.InRange(data.Length, 1, WindowsSupervisorProtocol.OutputChunkBytes);
                totalBytes += data.Length;
                Assert.InRange(totalBytes, 1, WindowsSupervisorProtocol.MaximumOutputBytes);
            }
            terminalReceived = kind == "exit";
            frames.Add(frame.Clone());
        }
        return frames;
    }

    private static void AssertReceipt(List<JsonElement> frames, int exitCode, string reason)
    {
        var receipt = Assert.Single(frames, frame => frame.GetProperty("kind").GetString() == "exit");
        Assert.Equal("exit", frames[^1].GetProperty("kind").GetString());
        Assert.Equal(WindowsSupervisorProtocol.Version, receipt.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(exitCode, receipt.GetProperty("exitCode").GetInt32());
        Assert.True(receipt.GetProperty("cleanupConfirmed").GetBoolean());
        Assert.Equal(0, receipt.GetProperty("activeProcesses").GetInt32());
        Assert.True(receipt.GetProperty("outputComplete").GetBoolean());
        Assert.Equal(reason, receipt.GetProperty("reason").GetString());
    }

    private static string DecodeOutput(IEnumerable<JsonElement> frames, string stream)
    {
        var bytes = frames.Where(frame => frame.GetProperty("kind").GetString() == "output" &&
                frame.GetProperty("stream").GetString() == stream)
            .SelectMany(frame => Convert.FromBase64String(frame.GetProperty("data").GetString()!)).ToArray();
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static IWindowsJobProcess StartTree(Workspace workspace)
    {
        var job = WindowsJobProcess.CreateSuspended(NodePath(), [Fixture("windows-supervisor-tree.cjs"), "tree", workspace.Root]);
        try
        {
            job.Resume();
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", name);

    private static string NodePath()
    {
        var executable = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "node.exe"))
            .FirstOrDefault(File.Exists);
        Assert.True(executable is not null, "Native tests require node.exe on PATH.");
        return Path.GetFullPath(executable!);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string reason)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(elapsed.Elapsed < WaitLimit, $"Timed out: {reason}");
            await Task.Delay(25);
        }
    }

    private static async Task<Process> CaptureAsync(Workspace workspace, string role)
    {
        var receipt = workspace.File(role + ".json");
        await WaitUntilAsync(() => File.Exists(receipt), $"{role} readiness");
        using var document = JsonDocument.Parse(File.ReadAllText(receipt));
        return Capture(document.RootElement.GetProperty("pid").GetInt32());
    }

    private static Process Capture(int processId)
    {
        Assert.NotEqual(Environment.ProcessId, processId);
        var process = Process.GetProcessById(processId);
        try
        {
            _ = process.Handle;
            Assert.False(process.HasExited);
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task AssertEmptyAsync(IWindowsJobProcess job, CapturedProcesses? processes = null)
    {
        if (processes is not null) await processes.AssertExitedAndReleaseAsync();
        await WaitUntilAsync(() => job.ActiveProcessCount == 0, "zero active job processes after releasing exited handles");
        for (var observation = 0; observation < 4; observation++)
        {
            await Task.Delay(100);
            Assert.Equal(0u, job.ActiveProcessCount);
        }
    }

    private sealed class HostSession : IAsyncDisposable
    {
        public Process Controller { get; }
        public Process Supervisor => _supervisor ?? throw new InvalidOperationException("Supervisor handle not captured.");
        private Process? _supervisor;
        private readonly Task<string> _stderr;

        public static async Task<HostSession> StartAsync(Workspace workspace)
        {
            var session = new HostSession(workspace);
            try
            {
                session._supervisor = await CaptureAsync(workspace, "supervisor");
                Assert.NotEqual(session.Controller.Id, session.Supervisor.Id);
                Assert.Equal(CliPath(), session.Supervisor.MainModule!.FileName, ignoreCase: true);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        private HostSession(Workspace workspace)
        {
            var start = new ProcessStartInfo(NodePath())
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = workspace.Root,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardInputEncoding = new UTF8Encoding(false)
            };
            start.ArgumentList.Add(Fixture("windows-supervisor-parent.cjs"));
            start.ArgumentList.Add(CliPath());
            start.ArgumentList.Add(workspace.Root);
            start.Environment.Clear();
            foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "PATHEXT", "COMSPEC", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" })
            {
                if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
            }
            foreach (var name in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "DOTNET_CLI_HOME" })
                start.Environment[name] = workspace.Root;
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            Controller = Process.Start(start) ?? throw new InvalidOperationException("CLI host controller failed to start.");
            _ = Controller.Handle;
            _stderr = Controller.StandardError.ReadToEndAsync();
        }

        public async Task LaunchAsync(string[] arguments, int timeoutMs)
        {
            var launch = JsonSerializer.Serialize(new { protocolVersion = WindowsSupervisorProtocol.Version, arguments, timeoutMs });
            Assert.True(launch.Length <= WindowsSupervisorProtocol.MaximumControlCharacters);
            await Controller.StandardInput.WriteLineAsync(launch).WaitAsync(WaitLimit);
            await Controller.StandardInput.FlushAsync().WaitAsync(WaitLimit);
        }

        public async Task ResumeAsync()
        {
            await Controller.StandardInput.WriteLineAsync("{\"kind\":\"resume\"}").WaitAsync(WaitLimit);
            await Controller.StandardInput.FlushAsync().WaitAsync(WaitLimit);
        }

        public async Task AssertExitAsync(int exitCode)
        {
            await Supervisor.WaitForExitAsync().WaitAsync(WaitLimit);
            await Controller.WaitForExitAsync().WaitAsync(WaitLimit);
            Assert.Equal(exitCode, Supervisor.ExitCode);
            Assert.Equal(exitCode, Controller.ExitCode);
            Assert.Empty(await _stderr.WaitAsync(WaitLimit));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                try
                {
                    if (_supervisor is not null)
                    {
                        if (!_supervisor.HasExited) _supervisor.Kill();
                        await _supervisor.WaitForExitAsync().WaitAsync(WaitLimit);
                    }
                }
                finally
                {
                    if (!Controller.HasExited) Controller.Kill(entireProcessTree: _supervisor is null);
                    await Controller.WaitForExitAsync().WaitAsync(WaitLimit);
                    await _stderr.WaitAsync(WaitLimit);
                }
            }
            finally
            {
                _supervisor?.Dispose();
                Controller.Dispose();
            }
        }
    }

    private sealed class ConsolePipePair(NamedPipeServerStream server, SafeFileHandle client) : IDisposable
    {
        public NamedPipeServerStream Server { get; } = server;
        public SafeFileHandle Client { get; } = client;

        public static async Task<ConsolePipePair> CreateAsync(bool asynchronous = true)
        {
            var name = "mcpval-console-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 4096, 4096);
            using var deadline = new CancellationTokenSource(WaitLimit);
            var connection = server.WaitForConnectionAsync(deadline.Token);
            SafeFileHandle? client = null;
            try
            {
                client = NativePipeMethods.CreateFileW(@"\\.\pipe\" + name, 0xc0000000, 0, IntPtr.Zero,
                    3, 0x00100000u | (asynchronous ? 0x40000000u : 0u), IntPtr.Zero);
                if (client.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
                await connection;
                return new ConsolePipePair(server, client);
            }
            catch
            {
                deadline.Cancel();
                client?.Dispose();
                try { await connection; }
                catch (OperationCanceledException) { }
                finally { server.Dispose(); }
                throw;
            }
        }

        public void Dispose()
        {
            try { Client.Dispose(); }
            finally { Server.Dispose(); }
        }
    }

    private static class NativePipeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
            IntPtr attributes, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetHandleInformation(SafeFileHandle handle, out uint flags);
    }

    private sealed class CapturedProcesses : IDisposable
    {
        public List<Process> Items { get; } = [];

        public async Task CaptureTreeAsync(Workspace workspace)
        {
            foreach (var role in new[] { "leader", "child-a", "child-b", "grandchild" })
                Items.Add(await CaptureAsync(workspace, role));
            Assert.Equal(4, Items.Select(process => process.Id).Distinct().Count());
        }

        public async Task AssertExitedAndReleaseAsync()
        {
            await WaitUntilAsync(() =>
            {
                foreach (var process in Items.ToArray())
                {
                    if (!process.WaitForExit(0)) continue;
                    Assert.True(process.HasExited);
                    process.Dispose();
                    Items.Remove(process);
                }
                return Items.Count == 0;
            }, "retained process handles signaled and released");
        }

        public void Dispose()
        {
            var failures = new List<Exception>();
            foreach (var process in Items)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        Assert.True(process.WaitForExit(5000), "Owned captured process did not exit during failure cleanup.");
                    }
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Exception exception) { failures.Add(exception); }
                finally { process.Dispose(); }
            }
            if (failures.Count > 0) throw new AggregateException("Captured process cleanup failed.", failures);
        }
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "windows-supervisor-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public string File(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}