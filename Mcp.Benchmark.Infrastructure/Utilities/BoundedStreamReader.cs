using System.Text;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal enum BoundedStreamRetention
{
    First,
    Last
}

internal sealed record BoundedTextReadResult(string Text, long TotalBytes, bool IsTruncated);

internal sealed class BoundedByteBuffer
{
    private readonly byte[] _captured;
    private readonly BoundedStreamRetention _retention;
    private readonly object _sync = new();
    private int _capturedCount;
    private int _ringStart;
    private long _totalBytes;

    public BoundedByteBuffer(int maxBytes, BoundedStreamRetention retention)
    {
        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        _captured = new byte[maxBytes];
        _retention = retention;
    }

    public bool Append(ReadOnlySpan<byte> bytes)
    {
        lock (_sync)
        {
            var wasTruncated = _totalBytes > _captured.Length;
            _totalBytes += bytes.Length;

            if (_retention == BoundedStreamRetention.First)
            {
                var writable = Math.Min(bytes.Length, _captured.Length - _capturedCount);
                if (writable > 0)
                {
                    bytes[..writable].CopyTo(_captured.AsSpan(_capturedCount));
                    _capturedCount += writable;
                }
            }
            else
            {
                foreach (var value in bytes)
                {
                    if (_capturedCount < _captured.Length)
                    {
                        _captured[(_ringStart + _capturedCount) % _captured.Length] = value;
                        _capturedCount++;
                    }
                    else
                    {
                        _captured[_ringStart] = value;
                        _ringStart = (_ringStart + 1) % _captured.Length;
                    }
                }
            }

            return !wasTruncated && _totalBytes > _captured.Length;
        }
    }

    public BoundedTextReadResult Snapshot()
    {
        lock (_sync)
        {
            byte[] ordered;
            if (_retention == BoundedStreamRetention.Last && _capturedCount == _captured.Length && _ringStart > 0)
            {
                ordered = new byte[_capturedCount];
                _captured.AsSpan(_ringStart).CopyTo(ordered);
                _captured.AsSpan(0, _ringStart).CopyTo(ordered.AsSpan(_captured.Length - _ringStart));
            }
            else
            {
                ordered = _captured.AsSpan(0, _capturedCount).ToArray();
            }

            return new BoundedTextReadResult(
                Encoding.UTF8.GetString(ordered),
                _totalBytes,
                _totalBytes > _captured.Length);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _capturedCount = 0;
            _ringStart = 0;
            _totalBytes = 0;
            Array.Clear(_captured);
        }
    }
}

internal static class BoundedStreamReader
{
    public static async Task<BoundedTextReadResult> ReadUtf8Async(
        Stream stream,
        int maxBytes,
        BoundedStreamRetention retention,
        Action? onLimitExceeded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var captured = new BoundedByteBuffer(maxBytes, retention);
        var readBuffer = new byte[Math.Min(8192, maxBytes)];

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (captured.Append(readBuffer.AsSpan(0, read)))
                {
                    onLimitExceeded?.Invoke();
                }
            }
        }
        catch (Exception ex) when ((ex is IOException or ObjectDisposedException) && captured.Snapshot().IsTruncated)
        {
        }

        return captured.Snapshot();
    }
}