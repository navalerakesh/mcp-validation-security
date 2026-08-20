using System.Text;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class BoundedStreamReaderTests
{
    [Fact]
    public async Task ReadUtf8Async_FirstRetention_ShouldBoundMemoryAndNotifyOnce()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0123456789"));
        var notifications = 0;

        var result = await BoundedStreamReader.ReadUtf8Async(
            stream,
            maxBytes: 4,
            BoundedStreamRetention.First,
            () => Interlocked.Increment(ref notifications),
            CancellationToken.None);

        result.Text.Should().Be("0123");
        result.TotalBytes.Should().Be(10);
        result.IsTruncated.Should().BeTrue();
        notifications.Should().Be(1);
    }

    [Fact]
    public async Task ReadUtf8Async_LastRetention_ShouldRetainTailWithinBound()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0123456789"));

        var result = await BoundedStreamReader.ReadUtf8Async(
            stream,
            maxBytes: 4,
            BoundedStreamRetention.Last,
            onLimitExceeded: null,
            CancellationToken.None);

        result.Text.Should().Be("6789");
        result.TotalBytes.Should().Be(10);
        result.IsTruncated.Should().BeTrue();
    }
}
