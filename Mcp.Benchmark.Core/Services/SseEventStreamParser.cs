using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class SseEventStreamParser
{
    public static IReadOnlyList<SseEventRecord> Parse(string? eventStream)
    {
        if (string.IsNullOrWhiteSpace(eventStream))
        {
            return Array.Empty<SseEventRecord>();
        }

        var events = new List<SseEventRecord>();
        var dataLines = new List<string>();
        string? eventName = null;
        string? eventId = null;
        int? retryMilliseconds = null;
        var hasFields = false;

        foreach (var rawLine in EnumerateLines(eventStream))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                FlushEvent();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            var field = separatorIndex >= 0 ? line[..separatorIndex] : line;
            var value = separatorIndex >= 0 ? line[(separatorIndex + 1)..] : string.Empty;
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            hasFields = true;
            switch (field)
            {
                case "data":
                    dataLines.Add(value);
                    break;
                case "event":
                    eventName = value;
                    break;
                case "id":
                    eventId = value.Contains('\0', StringComparison.Ordinal) ? eventId : value;
                    break;
                case "retry":
                    if (int.TryParse(value, out var parsedRetry) && parsedRetry >= 0)
                    {
                        retryMilliseconds = parsedRetry;
                    }
                    break;
            }
        }

        FlushEvent();
        return events;

        void FlushEvent()
        {
            if (!hasFields)
            {
                return;
            }

            events.Add(new SseEventRecord
            {
                Id = eventId,
                Event = eventName,
                Data = string.Join('\n', dataLines),
                RetryMilliseconds = retryMilliseconds
            });

            dataLines.Clear();
            eventName = null;
            retryMilliseconds = null;
            hasFields = false;
        }
    }

    private static IEnumerable<string> EnumerateLines(string content)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }
}