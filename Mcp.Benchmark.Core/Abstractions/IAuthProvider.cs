using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a contract for providing authentication tokens to the MCP validator.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Gets a valid access token for the current identity.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token string.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the name of the identity being used (e.g., "admin", "read-only").
    /// </summary>
    string IdentityName { get; }
}
