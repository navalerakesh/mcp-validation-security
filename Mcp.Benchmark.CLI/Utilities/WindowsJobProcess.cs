using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Microsoft.Win32.SafeHandles;

namespace Mcp.Benchmark.CLI.Utilities;

internal sealed class WindowsJobProcess : IWindowsJobProcess
{
    private const uint CreationFlags = 0x00000004 | 0x00080000;
    private const uint JobLimitFlags = 0x00002000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const nuint JobListAttribute = 0x0002000D;
    private const nuint HandleListAttribute = 0x00020002;
    private const int BasicAccountingInformation = 1;
    private const int ExtendedLimitInformation = 9;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;

    private readonly object _sync = new();
    private SafeKernelHandle _job = new();
    private readonly SafeKernelHandle _process = new();
    private readonly SafeKernelHandle _thread = new();
    private NamedPipeServerStream? _standardOutput;
    private NamedPipeServerStream? _standardError;
    private int? _exitCode;
    private bool _membershipVerified;
    private bool _resumed;
    private bool _terminationRequested;
    private bool _disposed;

    private WindowsJobProcess() { }

    internal static IWindowsJobProcess CreateSuspended(string absoluteExecutable, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new PlatformNotSupportedException("Windows job supervision requires Windows 10 or later.");

        var commandLine = PrepareCommandLine(absoluteExecutable, arguments);
        var owner = new WindowsJobProcess();
        try
        {
            owner.Initialize(absoluteExecutable, commandLine);
            return owner;
        }
        catch (Exception startupFailure)
        {
            owner.AbortStartup(startupFailure);
            throw;
        }
    }

    internal static string PrepareCommandLine(string absoluteExecutable, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(absoluteExecutable) || absoluteExecutable.Contains('\0') ||
            absoluteExecutable.Contains('"') || !Path.IsPathFullyQualified(absoluteExecutable))
            throw new ArgumentException("Supervisor executable must be an absolute application path.", nameof(absoluteExecutable));
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > WindowsSupervisorProtocol.MaximumArguments)
            throw new InvalidDataException("Supervisor argument count exceeds its limit.");
        var snapshot = arguments.ToArray();
        if (snapshot.Any(argument => argument is null))
            throw new InvalidDataException("Supervisor argument is missing.");
        return WindowsSupervisorProtocol.BuildCommandLine(absoluteExecutable, snapshot);
    }

    public Stream StandardOutput
    {
        get { lock (_sync) { ThrowIfDisposed(); return _standardOutput!; } }
    }

    public Stream StandardError
    {
        get { lock (_sync) { ThrowIfDisposed(); return _standardError!; } }
    }

    private void Initialize(string absoluteExecutable, string commandLine)
    {
        _job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid) throw NativeFailure("CreateJobObjectW");
        var limits = new JobExtendedLimitInformation
        {
            BasicLimitInformation = new JobBasicLimitInformation { LimitFlags = JobLimitFlags }
        };
        if (!NativeMethods.SetInformationJobObject(_job, ExtendedLimitInformation, ref limits,
                (uint)Marshal.SizeOf<JobExtendedLimitInformation>()))
            throw NativeFailure("SetInformationJobObject");

        using var standardInput = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var outputClient = CreateOutputPipe(out _standardOutput);
        using var errorClient = CreateOutputPipe(out _standardError);
        using (var attributes = ProcessAttributes.Create(_job, standardInput.ClientSafePipeHandle,
               outputClient.SafePipeHandle, errorClient.SafePipeHandle))
        {
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardInput = standardInput.ClientSafePipeHandle.DangerousGetHandle(),
                    StandardOutput = outputClient.SafePipeHandle.DangerousGetHandle(),
                    StandardError = errorClient.SafePipeHandle.DangerousGetHandle()
                },
                AttributeList = attributes.DangerousGetHandle()
            };
            if (!NativeMethods.CreateProcessW(absoluteExecutable, new StringBuilder(commandLine),
                    IntPtr.Zero, IntPtr.Zero, true, CreationFlags, IntPtr.Zero, null, ref startup, out var processInfo))
                throw NativeFailure("CreateProcessW");
            _process.Initialize(processInfo.Process);
            _thread.Initialize(processInfo.Thread);
        }

        standardInput.DisposeLocalCopyOfClientHandle();
        outputClient.Dispose();
        errorClient.Dispose();
        VerifyMembership();
    }

    private static NamedPipeClientStream CreateOutputPipe(out NamedPipeServerStream server)
    {
        var name = "mcpval-worker-" + Guid.NewGuid().ToString("N");
        server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance,
            WindowsSupervisorProtocol.OutputChunkBytes, 0);
        NamedPipeClientStream? client = null;
        using var deadline = new CancellationTokenSource(WindowsSupervisorProtocol.StartupTimeoutMilliseconds);
        var connection = server.WaitForConnectionAsync(deadline.Token);
        try
        {
            client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly,
                TokenImpersonationLevel.Anonymous, HandleInheritability.Inheritable);
            client.Connect(WindowsSupervisorProtocol.StartupTimeoutMilliseconds);
            connection.GetAwaiter().GetResult();
            return client;
        }
        catch
        {
            deadline.Cancel();
            client?.Dispose();
            try { connection.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            finally { server.Dispose(); }
            throw;
        }
    }

    private void VerifyMembership()
    {
        _membershipVerified = false;
        if (!NativeMethods.IsProcessInJob(_process, _job, out var belongsToJob))
            throw NativeFailure("IsProcessInJob");
        if (!belongsToJob)
            throw new InvalidOperationException("Suspended process is not a member of its owned job.");
        _membershipVerified = true;
    }

    public void Resume()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_resumed || _terminationRequested || _exitCode.HasValue)
                throw new InvalidOperationException("Supervisor process cannot be resumed in its current state.");
            try
            {
                VerifyMembership();
                var previousSuspendCount = NativeMethods.ResumeThread(_thread);
                if (previousSuspendCount == uint.MaxValue) throw NativeFailure("ResumeThread");
                if (previousSuspendCount != 1)
                    throw new InvalidOperationException("Supervisor thread has an unexpected suspend count.");
                _resumed = true;
                _thread.CloseChecked();
            }
            catch (Exception startupFailure)
            {
                AbortStartup(startupFailure);
                throw;
            }
        }
    }

    public bool TryGetExitCode(out int exitCode)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_exitCode is int captured)
            {
                exitCode = captured;
                return true;
            }
            exitCode = default;
            var waitResult = NativeMethods.WaitForSingleObject(_process, 0);
            if (waitResult == WaitTimeout) return false;
            if (waitResult == WaitFailed) throw NativeFailure("WaitForSingleObject");
            if (waitResult != WaitObject0)
                throw new InvalidOperationException("Supervisor process wait returned an unexpected result.");
            if (!NativeMethods.GetExitCodeProcess(_process, out var nativeExitCode))
                throw NativeFailure("GetExitCodeProcess");
            exitCode = unchecked((int)nativeExitCode);
            _exitCode = exitCode;
            ReleaseLeaderHandles();
            return true;
        }
    }

    public uint ActiveProcessCount
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                TryGetExitCode(out _);
                var size = (uint)Marshal.SizeOf<JobBasicAccountingInformation>();
                if (!NativeMethods.QueryInformationJobObject(_job, BasicAccountingInformation,
                        out var accounting, size, out var returnedSize))
                    throw NativeFailure("QueryInformationJobObject");
                if (returnedSize != size)
                    throw new InvalidDataException("Job accounting returned an unexpected structure size.");
                return accounting.ActiveProcesses;
            }
        }
    }

    public void Terminate(int exitCode)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!NativeMethods.TerminateJobObject(_job, unchecked((uint)exitCode)))
                throw NativeFailure("TerminateJobObject");
            _terminationRequested = true;
        }
    }

    private void AbortStartup(Exception startupFailure)
    {
        var failures = new List<Exception> { startupFailure };
        try
        {
            if (!_membershipVerified && !_process.IsInvalid && !_process.IsClosed &&
                !NativeMethods.TerminateProcess(_process, 1))
                throw NativeFailure("TerminateProcess");
        }
        catch (Exception terminationFailure)
        {
            failures.Add(terminationFailure);
        }
        finally
        {
            try { Dispose(); }
            catch (Exception disposalFailure) { failures.Add(disposalFailure); }
        }
        if (failures.Count > 1)
            throw new AggregateException("Supervisor startup and owned-process cleanup failed.", failures);
    }

    private void ReleaseLeaderHandles()
    {
        try { _thread.CloseChecked(); }
        finally { _process.CloseChecked(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try { _job.CloseChecked(); }
            finally
            {
                try { ReleaseLeaderHandles(); }
                finally
                {
                    try { _standardOutput?.Dispose(); }
                    finally { _standardError?.Dispose(); }
                }
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static Win32Exception NativeFailure(string operation) => NativeFailure(operation, Marshal.GetLastPInvokeError());

    private static Win32Exception NativeFailure(string operation, int error) =>
        new(error, $"{operation} failed (Win32 error {error}).");

    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private int _closeError;

        public SafeKernelHandle() : base(true) { }

        internal void Initialize(IntPtr value) => SetHandle(value);

        internal void CloseChecked()
        {
            Dispose();
            if (_closeError != 0) throw NativeFailure("CloseHandle", _closeError);
        }

        protected override bool ReleaseHandle()
        {
            var closed = NativeMethods.CloseHandle(handle);
            if (!closed) _closeError = Marshal.GetLastPInvokeError();
            return closed;
        }
    }

    private sealed class ProcessAttributes : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly SafeHandle[] _handles;
        private int _references;
        private IntPtr _values;
        private bool _initialized;

        private ProcessAttributes(SafeHandle[] handles) : base(true) => _handles = handles;

        internal static ProcessAttributes Create(params SafeHandle[] handles)
        {
            var attributes = new ProcessAttributes(handles);
            try
            {
                attributes.Initialize();
                return attributes;
            }
            catch
            {
                attributes.Dispose();
                throw;
            }
        }

        private void Initialize()
        {
            nuint size = 0;
            var sized = NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            var error = Marshal.GetLastPInvokeError();
            if (sized || error != 122) throw NativeFailure("InitializeProcThreadAttributeList", error);
            if (size == 0 || size > int.MaxValue)
                throw new InvalidDataException("Process attribute allocation has an invalid size.");
            SetHandle(Marshal.AllocHGlobal((int)size));
            if (!NativeMethods.InitializeProcThreadAttributeList(handle, 2, 0, ref size))
                throw NativeFailure("InitializeProcThreadAttributeList");
            _initialized = true;
            _values = Marshal.AllocHGlobal(4 * IntPtr.Size);
            for (var index = 0; index < _handles.Length; index++)
            {
                var acquired = false;
                _handles[index].DangerousAddRef(ref acquired);
                if (acquired) _references++;
                Marshal.WriteIntPtr(_values, index * IntPtr.Size, _handles[index].DangerousGetHandle());
            }
            if (!NativeMethods.UpdateProcThreadAttribute(handle, 0, JobListAttribute, _values,
                    (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw NativeFailure("UpdateProcThreadAttribute(JOB_LIST)");
            if (!NativeMethods.UpdateProcThreadAttribute(handle, 0, HandleListAttribute,
                    IntPtr.Add(_values, IntPtr.Size), (nuint)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw NativeFailure("UpdateProcThreadAttribute(HANDLE_LIST)");
        }

        protected override bool ReleaseHandle()
        {
            if (_initialized) NativeMethods.DeleteProcThreadAttributeList(handle);
            Marshal.FreeHGlobal(_values);
            Marshal.FreeHGlobal(handle);
            for (var index = 0; index < _references; index++) _handles[index].DangerousRelease();
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeKernelHandle CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeKernelHandle job, int informationClass,
            ref JobExtendedLimitInformation information, uint length);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeKernelHandle job, int informationClass,
            out JobBasicAccountingInformation information, uint length, out uint returnLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute,
            IntPtr value, nuint size, IntPtr previousValue, IntPtr returnSize);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(string application, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsProcessInJob(SafeKernelHandle process, SafeKernelHandle job,
            [MarshalAs(UnmanagedType.Bool)] out bool result);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint ResumeThread(SafeKernelHandle thread);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeKernelHandle process, uint milliseconds);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint exitCode);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeKernelHandle job, uint exitCode);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(SafeKernelHandle process, uint exitCode);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}