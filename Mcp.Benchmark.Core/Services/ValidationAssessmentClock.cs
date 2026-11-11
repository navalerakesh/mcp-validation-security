using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationAssessmentClock
{
    public static DateTimeOffset GetTimestamp(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var recordedTime = result.EndTime ?? result.StartTime;
        if (recordedTime.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("Recorded assessment time must have an explicit timezone.", nameof(result));
        return new DateTimeOffset(recordedTime).ToUniversalTime();
    }
}