using System.Diagnostics;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ValidatorProcessResourceSampler : IValidatorProcessResourceSampler
{
    public ValidatorProcessResourceSnapshot? Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new ValidatorProcessResourceSnapshot(process.TotalProcessorTime, process.WorkingSet64);
    }
}