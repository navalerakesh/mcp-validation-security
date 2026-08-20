using System.Net;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Core;

public sealed class NetworkAddressClassifierTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b:1::7f00:1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    [InlineData("::127.0.0.1")]
    public void IsRestricted_PrivateLocalReservedAndMappedAddresses_ReturnTrue(string value)
    {
        NetworkAddressClassifier.IsRestricted(IPAddress.Parse(value)).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void IsRestricted_PublicAddresses_ReturnFalse(string value)
    {
        NetworkAddressClassifier.IsRestricted(IPAddress.Parse(value)).Should().BeFalse();
    }

    [Theory]
    [InlineData("BÜCHER.Example.", "xn--bcher-kva.example")]
    [InlineData("[2001:4860:4860::8888]", "2001:4860:4860::8888")]
    [InlineData("Example.TEST", "example.test")]
    public void NormalizeHost_EquivalentForms_ReturnCanonicalHost(string value, string expected)
    {
        NetworkTargetPolicy.NormalizeHost(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://Example.TEST/path", "https://example.test:443")]
    [InlineData("http://example.test/path", "http://example.test:80")]
    [InlineData("https://example.test:8443/path", "https://example.test:8443")]
    [InlineData("https://[2001:4860:4860::8888]/path", "https://[2001:4860:4860::8888]:443")]
    public void NormalizeOrigin_EquivalentUris_ReturnSchemeHostAndEffectivePort(string value, string expected)
    {
        NetworkTargetPolicy.NormalizeOrigin(value).Should().Be(expected);
    }
}