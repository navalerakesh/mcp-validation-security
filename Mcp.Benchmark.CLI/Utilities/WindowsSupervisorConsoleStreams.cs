using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mcp.Benchmark.CLI.Utilities;

internal sealed class WindowsSupervisorConsoleStreams : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly object _sync = new();
    private readonly object _disposeSync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly StreamReader _reader;
    private bool _disposed;
    private int _operations;

    private WindowsSupervisorConsoleStreams(FileStream input, FileStream output)
    {
        _input = input;
        _output = output;
        _reader = new StreamReader(input, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        Input = new InputReader(this);
        Output = new OutputWriter(this);
    }

    public TextReader Input { get; }
    public TextWriter Output { get; }

    internal static WindowsSupervisorConsoleStreams Open()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows supervisor console pipes require Windows.");
        using var input = new SafeFileHandle(NativeMethods.GetStdHandle(-10), ownsHandle: false);
        using var output = new SafeFileHandle(NativeMethods.GetStdHandle(-11), ownsHandle: false);
        return Open(input, output);
    }

    internal static WindowsSupervisorConsoleStreams Open(SafeFileHandle input, SafeFileHandle output)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows supervisor console pipes require Windows.");
        var ownedInput = DuplicatePipe(input, FileAccess.Read);
        FileStream? ownedOutput = null;
        try
        {
            ownedOutput = DuplicatePipe(output, FileAccess.Write);
            return new WindowsSupervisorConsoleStreams(ownedInput, ownedOutput);
        }
        catch
        {
            ownedInput.Dispose();
            ownedOutput?.Dispose();
            throw;
        }
    }

    private static FileStream DuplicatePipe(SafeFileHandle source, FileAccess access)
    {
        if (source.IsInvalid || source.IsClosed)
            throw new IOException("Windows supervisor standard pipe handle is unavailable.");
        var process = NativeMethods.GetCurrentProcess();
        if (!NativeMethods.DuplicateHandle(process, source, process, out var duplicate, 0, false, 2))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot duplicate supervisor standard pipe.");
        try
        {
            if (NativeMethods.GetFileType(duplicate) != 3 ||
                !NativeMethods.GetNamedPipeInfo(duplicate, out var flags, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) ||
                (flags & 4) != 0 || !duplicate.IsAsync)
                throw new IOException("Windows supervisor requires overlapped byte-mode standard pipes. Use the Windows wrapper with overlapped stdio.");
            return new FileStream(duplicate, access, bufferSize: 1, isAsync: true);
        }
        catch
        {
            duplicate.Dispose();
            throw;
        }
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _operations++;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            linked.Token.ThrowIfCancellationRequested();
            return await operation(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (--_operations == 0) Monitor.PulseAll(_sync);
            }
        }
    }

    private Task WriteAsync(ReadOnlyMemory<char> text, bool newline, CancellationToken token) => RunAsync(async linked =>
    {
        await _writeGate.WaitAsync(linked).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(Utf8.GetBytes(text.ToString()), linked).ConfigureAwait(false);
            if (newline) await _output.WriteAsync("\n"u8.ToArray(), linked).ConfigureAwait(false);
            return true;
        }
        finally { _writeGate.Release(); }
    }, token);

    public void Dispose()
    {
        lock (_disposeSync)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _stop.Cancel();
            lock (_sync)
            {
                while (_operations != 0) Monitor.Wait(_sync);
            }
            try { _reader.Dispose(); }
            finally
            {
                try { _input.Dispose(); }
                finally
                {
                    try { _output.Dispose(); }
                    finally
                    {
                        _writeGate.Dispose();
                        _stop.Dispose();
                    }
                }
            }
        }
    }

    private sealed class InputReader(WindowsSupervisorConsoleStreams owner) : TextReader
    {
        public override int Peek() => throw new NotSupportedException("Use asynchronous supervisor input.");
        public override int Read() => throw new NotSupportedException("Use asynchronous supervisor input.");
        public override int Read(char[] buffer, int index, int count) => throw new NotSupportedException("Use asynchronous supervisor input.");
        public override Task<int> ReadAsync(char[] buffer, int index, int count) => ReadAsync(buffer.AsMemory(index, count)).AsTask();
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            new(owner.RunAsync(token => owner._reader.ReadAsync(buffer, token).AsTask(), cancellationToken));
        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            new(owner.RunAsync(token => owner._reader.ReadLineAsync(token).AsTask(), cancellationToken));
        public override Task<string> ReadToEndAsync() => ReadToEndAsync(CancellationToken.None);
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
            owner.RunAsync(token => owner._reader.ReadToEndAsync(token), cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) owner.Dispose(); }
    }

    private sealed class OutputWriter(WindowsSupervisorConsoleStreams owner) : TextWriter
    {
        public override Encoding Encoding => Utf8;
        public override void Write(char value) => throw new NotSupportedException("Use asynchronous supervisor output.");
        public override void Flush() => throw new NotSupportedException("Use asynchronous supervisor output.");
        public override Task WriteAsync(char value) => WriteAsync(value.ToString());
        public override Task WriteAsync(string? value) => WriteAsync(value.AsMemory());
        public override Task WriteAsync(char[] buffer, int index, int count) => WriteAsync(buffer.AsMemory(index, count));
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
            owner.WriteAsync(buffer, false, cancellationToken);
        public override Task WriteLineAsync() => WriteLineAsync(ReadOnlyMemory<char>.Empty);
        public override Task WriteLineAsync(string? value) => WriteLineAsync(value.AsMemory());
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
            owner.WriteAsync(buffer, true, cancellationToken);
        public override Task FlushAsync() => FlushAsync(CancellationToken.None);
        public override Task FlushAsync(CancellationToken cancellationToken) => owner.RunAsync(async token =>
        {
            await owner._writeGate.WaitAsync(token).ConfigureAwait(false);
            owner._writeGate.Release();
            return true;
        }, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) owner.Dispose(); }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess,
            out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetFileType(SafeFileHandle handle);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeInfo(SafeFileHandle handle, out uint flags,
            IntPtr outputBufferSize, IntPtr inputBufferSize, IntPtr maximumInstances);
    }
}