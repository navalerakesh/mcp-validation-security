using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Authentication
{
    public interface IAuthenticationStrategy
    {
        string ProviderName { get; }
        bool CanHandle(AuthMetadata metadata);
        Task<string?> AcquireTokenAsync(string scope, AuthMetadata metadata, bool isInteractive, CancellationToken ct, string? tenantId = null, string? clientId = null);
    }
}
