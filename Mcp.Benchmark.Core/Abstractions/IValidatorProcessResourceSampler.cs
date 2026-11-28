using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public interface IValidatorProcessResourceSampler
{
    ValidatorProcessResourceSnapshot? Capture();
}