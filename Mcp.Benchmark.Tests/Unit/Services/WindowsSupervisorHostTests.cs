using System.Text.Json;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class WindowsSupervisorHostTests
{
    private const string Launch = "{\"protocolVersion\":1,\"arguments\":[\"validate\"],\"timeoutMs\":1000}\n";

    [Fact]
    public async Task CreationConsumingDeadline_ShouldNeverResumeWorker()
    {
        var clock = new ControlledClock();
        var job = new FakeJob { LeaderExitCode = 0 };
        var output = new StringWriter();
        var code = await RunReadyAsync(new OpenControlReader(Launch), output, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(20));
            return job;
        }, clock);
        code.Should().Be(124);
        job.Resumed.Should().BeFalse();
        job.Terminated.Should().BeTrue();
    }

    [Fact]
    public async Task LeaderExit_WithDescendants_ShouldTerminateAndObserveZeroBeforeReceipt()
    {
        var job = new FakeJob { LeaderExitCode = 7 };
        var output = new StringWriter();
        var code = await RunReadyAsync(new OpenControlReader(Launch), output, _ => job);
        code.Should().Be(7);
        job.Terminated.Should().BeTrue();
        job.ZeroObserved.Should().BeTrue();
        job.Disposed.Should().BeTrue();
        Terminal(output).GetProperty("cleanupConfirmed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Completion_ShouldDrainPendingParentReadBeforeReturning()
    {
        var input = new OpenControlReader(Launch);
        var output = new StringWriter();
        var code = await RunReadyAsync(input, output, _ => new FakeJob { LeaderExitCode = 0 });
        code.Should().Be(0);
        input.PendingReadFinished.Should().BeTrue();
    }

    [Fact]
    public async Task Deadline_ShouldCancelAndDrainBlockedOutputBeforeReturning()
    {
        var input = new OpenControlReader(Launch);
        var clock = new ControlledClock();
        var output = new StringWriter();
        var blocked = new BlockingOutputWriter(output, input.AllowResume);
        var job = new FakeJob
        {
            Output = new MemoryStream([1, 2, 3]),
            OnExitProbe = () => clock.Advance(TimeSpan.FromSeconds(2)),
            OnQuery = () => clock.Advance(TimeSpan.FromSeconds(6))
        };
        var run = WindowsSupervisorHost.RunAsync(input, blocked, _ => job, clock);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        blocked.FailOutput();
        var code = await run.WaitAsync(TimeSpan.FromSeconds(5));
        code.Should().Be(70);
        blocked.Finished.Should().BeTrue();
        input.PendingReadFinished.Should().BeTrue();
        job.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminationWithoutZeroEvidence_ShouldNeverConfirmCleanup(bool terminateThrows)
    {
        var clock = new ControlledClock();
        var job = new FakeJob
        {
            LeaderExitCode = 0, TerminationFails = terminateThrows, KeepAliveAfterTerminate = true,
            OnQuery = () => clock.Advance(TimeSpan.FromSeconds(6))
        };
        var output = new StringWriter();
        var code = await RunReadyAsync(new OpenControlReader(Launch), output, _ => job, clock);
        code.Should().Be(70);
        Terminal(output).GetProperty("cleanupConfirmed").GetBoolean().Should().BeFalse();
        Terminal(output).GetProperty("activeProcesses").ValueKind.Should().Be(JsonValueKind.Null);
        job.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Deadline_ShouldTerminateOwnedJobWithoutWallClockAssumptions()
    {
        var clock = new ControlledClock();
        var job = new FakeJob { OnExitProbe = () => clock.Advance(TimeSpan.FromSeconds(2)) };
        var output = new StringWriter();
        var code = await RunReadyAsync(new OpenControlReader(Launch), output, _ => job, clock);
        code.Should().Be(124);
        job.Resumed.Should().BeTrue();
        job.Terminated.Should().BeTrue();
        Terminal(output).GetProperty("reason").GetString().Should().Be("deadline");
    }

    [Theory]
    [InlineData("{\"kind\":\"cancel\"}\n", 124)]
    [InlineData("{\"kind\":\"cancel\",\"unexpected\":true}\n", 70)]
    [InlineData("{\"kind\":\"cancel\",\"kind\":\"cancel\"}\n", 70)]
    public async Task ParentControl_ShouldBeStrictAndNeverAllowAnotherLaunch(string control, int expected)
    {
        var job = new FakeJob();
        var created = false;
        var output = new StringWriter();
        var code = await WindowsSupervisorHost.RunAsync(new StringReader(Launch + control), output, _ => { created = true; return job; });
        code.Should().Be(expected);
        created.Should().BeFalse();
        job.Resumed.Should().BeFalse();
    }

    [Fact]
    public async Task OutputBudget_ShouldTerminateRatherThanForwardUnboundedBytes()
    {
        var job = new FakeJob { Output = new MemoryStream(new byte[1024 * 1024 + 1]) };
        var output = new StringWriter();
        var code = await RunReadyAsync(new OpenControlReader(Launch), output, _ => job);
        code.Should().Be(70);
        job.Terminated.Should().BeTrue();
        Terminal(output).GetProperty("outputComplete").GetBoolean().Should().BeFalse();
        var forwarded = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            forwarded.Where(frame => frame.RootElement.GetProperty("kind").GetString() == "output")
                .Sum(frame => Convert.FromBase64String(frame.RootElement.GetProperty("data").GetString()!).Length).Should().BeLessThanOrEqualTo(1024 * 1024);
        }
        finally { foreach (var frame in forwarded) frame.Dispose(); }
    }

    [Fact]
    public async Task MalformedLaunch_ShouldNeverCreateWorker()
    {
        var created = false;
        var output = new StringWriter();
        var code = await WindowsSupervisorHost.RunAsync(new StringReader("{}\n"), output, _ => { created = true; throw new InvalidOperationException(); });
        created.Should().BeFalse();
        code.Should().Be(64);
        using var terminal = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        terminal.RootElement.GetProperty("cleanupConfirmed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ParentEof_ShouldTerminateAndObserveOwnedJobBeforeConfirmingCleanup()
    {
        var job = new FakeJob();
        var created = false;
        var output = new StringWriter();
        var code = await WindowsSupervisorHost.RunAsync(new StringReader(Launch), output, _ => { created = true; return job; });
        code.Should().Be(124);
        created.Should().BeFalse();
        job.Resumed.Should().BeFalse();
        using var terminal = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        terminal.RootElement.GetProperty("cleanupConfirmed").GetBoolean().Should().BeTrue();
        terminal.RootElement.GetProperty("activeProcesses").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ParentLossDuringCreation_ShouldTerminateSuspendedWorkerWithoutResume()
    {
        var reader = new ClosingControlReader(Launch);
        var job = new FakeJob();
        var output = new StringWriter();
        var code = await WindowsSupervisorHost.RunAsync(reader, output, _ => { reader.CloseParent(); return job; });
        code.Should().Be(124);
        job.Resumed.Should().BeFalse();
        job.Terminated.Should().BeTrue();
        job.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ParentLossDuringFinalAdmissionCheck_ShouldNeverResume()
    {
        var input = new OpenControlReader(Launch);
        var clock = new ControlledClock();
        var output = new StringWriter();
        var job = new FakeJob();
        var writer = new ReadyWriter(output, () =>
        {
            input.AllowResume();
            clock.OnTimestamp = input.CloseParent;
        });
        var code = await WindowsSupervisorHost.RunAsync(input, writer, _ => job, clock);
        code.Should().Be(124);
        job.Resumed.Should().BeFalse();
        job.Terminated.Should().BeTrue();
    }

    [Fact]
    public async Task ParentDecoderFailureDuringAdmission_ShouldNotResumeAndMustEmitFailureReceipt()
    {
        var input = new OpenControlReader(Launch);
        var clock = new ControlledClock();
        var output = new StringWriter();
        var job = new FakeJob();
        var writer = new ReadyWriter(output, () =>
        {
            input.AllowResume();
            clock.OnTimestamp = input.FailDecoding;
        });
        var code = await WindowsSupervisorHost.RunAsync(input, writer, _ => job, clock);
        code.Should().Be(70);
        job.Resumed.Should().BeFalse();
        job.Terminated.Should().BeTrue();
        Terminal(output).GetProperty("exitCode").GetInt32().Should().Be(70);
    }

    private sealed class FakeJob : IWindowsJobProcess
    {
        public Stream Output { get; init; } = new MemoryStream();
        public Stream StandardOutput => Output;
        public Stream StandardError { get; } = new MemoryStream();
        public int? LeaderExitCode { get; init; }
        public bool TerminationFails { get; init; }
        public bool KeepAliveAfterTerminate { get; init; }
        public Action? OnQuery { get; init; }
        public Action? OnExitProbe { get; init; }
        public bool Terminated { get; private set; }
        public bool Resumed { get; private set; }
        public bool Disposed { get; private set; }
        public bool ZeroObserved { get; private set; }
        public uint ActiveProcessCount
        {
            get
            {
                OnQuery?.Invoke();
                var active = Terminated && !KeepAliveAfterTerminate ? 0u : 1u;
                ZeroObserved |= active == 0;
                return active;
            }
        }
        public void Resume() => Resumed = true;
        public bool TryGetExitCode(out int exitCode) { OnExitProbe?.Invoke(); exitCode = LeaderExitCode ?? 0; return LeaderExitCode.HasValue; }
        public void Terminate(int exitCode)
        {
            if (TerminationFails) throw new IOException("synthetic termination failure");
            Terminated = true;
        }
        public void Dispose() { Disposed = true; StandardOutput.Dispose(); StandardError.Dispose(); }
    }

    private static JsonElement Terminal(StringWriter output)
    {
        using var document = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        return document.RootElement.Clone();
    }

    private sealed class ControlledClock : TimeProvider
    {
        private long _ticks;
        public Action? OnTimestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { var action = OnTimestamp; OnTimestamp = null; action?.Invoke(); return _ticks; }
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    private static Task<int> RunReadyAsync(OpenControlReader input, StringWriter output,
        Func<IReadOnlyList<string>, IWindowsJobProcess> create, TimeProvider? clock = null) =>
        WindowsSupervisorHost.RunAsync(input, new ReadyWriter(output, input.AllowResume), create, clock);

    private sealed class ReadyWriter(StringWriter output, Action resume) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            await output.WriteLineAsync(buffer, cancellationToken);
            using var frame = JsonDocument.Parse(buffer.ToString());
            if (frame.RootElement.GetProperty("kind").GetString() == "ready") resume();
        }
    }

    private sealed class OpenControlReader(string initial) : TextReader
    {
        private readonly StringReader _initial = new(initial);
        private readonly StringReader _resumeFrame = new("{\"kind\":\"resume\"}\n");
        private readonly TaskCompletionSource _resume = new();
        private readonly TaskCompletionSource<int> _closed = new();
        public void CloseParent() => _closed.TrySetResult(0);
        public void FailDecoding() => _closed.TrySetException(new System.Text.DecoderFallbackException("synthetic invalid UTF-8"));
        public bool PendingReadFinished { get; private set; }
        public void AllowResume() => _resume.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _initial.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) return read;
            await _resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            read = await _resumeFrame.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) return read;
            try { return await _closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally { PendingReadFinished = true; }
        }
    }

    private sealed class BlockingOutputWriter(StringWriter output, Action resume) : TextWriter
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Finished { get; private set; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public void FailOutput() => _release.TrySetException(new IOException("synthetic output failure"));
        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            using var frame = JsonDocument.Parse(buffer.ToString());
            var kind = frame.RootElement.GetProperty("kind").GetString();
            if (kind == "output")
            {
                Started.TrySetResult();
                try { await _release.Task.WaitAsync(cancellationToken); }
                finally { Finished = true; }
            }
            else
            {
                await output.WriteLineAsync(buffer, cancellationToken);
                if (kind == "ready") resume();
            }
        }
    }

    private sealed class ClosingControlReader(string initial) : TextReader
    {
        private readonly StringReader _initial = new(initial);
        private readonly TaskCompletionSource<int> _closed = new();
        public void CloseParent() => _closed.TrySetResult(0);
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _initial.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : await _closed.Task.WaitAsync(cancellationToken);
        }
    }
}