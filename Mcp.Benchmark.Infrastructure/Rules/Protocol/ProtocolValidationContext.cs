using Mcp.Benchmark.Core.Abstractions;

using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Infrastructure.Rules.Protocol;

public class ProtocolValidationContext
{
    public IMcpHttpClient Client { get; }
    public string Endpoint { get; }

    public ProtocolValidationContext(IMcpHttpClient client, string endpoint)
    {
        Client = client;
        Endpoint = endpoint;
    }
}
