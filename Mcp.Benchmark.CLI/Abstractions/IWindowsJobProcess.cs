namespace Mcp.Benchmark.CLI.Abstractions;

internal interface IWindowsJobProcess : IDisposable
{
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    void Resume();
    bool TryGetExitCode(out int exitCode);
    uint ActiveProcessCount { get; }
    void Terminate(int exitCode);
}