param([Parameter(Mandatory = $true)][string]$Root)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [IO.Path]::IsPathRooted($Root) -or [IO.Path]::GetFullPath($Root) -ne $Root) {
    throw 'Expected an absolute fixture directory.'
}

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowsSupervisorBreakawayProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
        IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static int Attempt()
    {
        string executable = System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfo)) };
        ProcessInformation process;
        bool created = CreateProcessW(executable, new StringBuilder("\"" + executable + "\" /d /c exit 0"),
            IntPtr.Zero, IntPtr.Zero, false, 0x01000004, IntPtr.Zero, null, ref startup, out process);
        int error = Marshal.GetLastWin32Error();
        if (!created) return error;
        try
        {
            if (!TerminateProcess(process.Process, 91)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (WaitForSingleObject(process.Process, 5000) != 0) throw new InvalidOperationException("Escaped suspended child cleanup failed.");
        }
        finally
        {
            bool threadClosed = CloseHandle(process.Thread);
            bool processClosed = CloseHandle(process.Process);
            if (!threadClosed || !processClosed) throw new InvalidOperationException("Probe handles failed to close.");
        }
        return 0;
    }
}
'@

$receipt = Join-Path $Root 'leader.json'
[IO.File]::WriteAllText("$receipt.tmp", (@{ pid = $PID } | ConvertTo-Json -Compress))
[IO.File]::Move("$receipt.tmp", $receipt)
$deadline = [Diagnostics.Stopwatch]::StartNew()
while (-not [IO.File]::Exists((Join-Path $Root 'release'))) {
    if ($deadline.Elapsed.TotalSeconds -ge 60) { throw 'Fixture release deadline exceeded.' }
    Start-Sleep -Milliseconds 20
}
$errorCode = [WindowsSupervisorBreakawayProbe]::Attempt()
@{ created = ($errorCode -eq 0); error = $errorCode } | ConvertTo-Json -Compress