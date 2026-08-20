using System.Net;
using System.Net.Sockets;
using System.Globalization;

namespace Mcp.Benchmark.Core.Services;

public static class NetworkAddressClassifier
{
    public static bool IsRestricted(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 198 && bytes[1] is 18 or 19) ||
                   (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                   (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
                   bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal ||
                   (bytes[0] & 0xFE) == 0xFC ||
                   IsPrefix(bytes, [0x00, 0x64, 0xFF, 0x9B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], prefixBits: 96) ||
                   IsPrefix(bytes, [0x00, 0x64, 0xFF, 0x9B, 0x00, 0x01], prefixBits: 48) ||
                   IsPrefix(bytes, [0x20, 0x02], prefixBits: 16) ||
                   IsPrefix(bytes, [0x20, 0x01, 0x00, 0x00], prefixBits: 32) ||
                   bytes.Take(12).All(value => value == 0) ||
                   (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
        }

        return true;
    }

    private static bool IsPrefix(byte[] address, byte[] prefix, int prefixBits)
    {
        var completeBytes = prefixBits / 8;
        return address.AsSpan(0, completeBytes).SequenceEqual(prefix.AsSpan(0, completeBytes));
    }
}

public static class NetworkTargetPolicy
{
    public static string NormalizeHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var candidate = host.Trim().Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(candidate, out var address))
        {
            return address.ToString().ToLowerInvariant();
        }

        return new IdnMapping().GetAscii(candidate).ToLowerInvariant();
    }

    public static string CreateHttpsOrigin(string host)
    {
        var normalizedHost = NormalizeHost(host);
        var authorityHost = normalizedHost.Contains(':', StringComparison.Ordinal)
            ? $"[{normalizedHost}]"
            : normalizedHost;
        return $"https://{authorityHost}:443";
    }

    public static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Allowed origin '{origin}' is not an absolute URI.", nameof(origin));
        }

        return NormalizeOrigin(uri);
    }

    public static string NormalizeOrigin(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"Origin scheme '{uri.Scheme}' is not supported.", nameof(uri));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Origins containing user-info are not allowed.", nameof(uri));
        }

        var host = NormalizeHost(uri.IdnHost);
        if (host.Contains(':', StringComparison.Ordinal))
        {
            host = $"[{host}]";
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{host}:{uri.Port}";
    }
}