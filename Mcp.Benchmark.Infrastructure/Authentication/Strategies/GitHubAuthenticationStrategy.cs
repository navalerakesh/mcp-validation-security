using System;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Authentication.Strategies
{
    public class GitHubAuthenticationStrategy : BaseCliStrategy, IAuthenticationStrategy
    {
        public string ProviderName => "GitHub";

        public GitHubAuthenticationStrategy(ILogger<GitHubAuthenticationStrategy> logger) : base(logger)
        {
        }

        public bool CanHandle(AuthMetadata metadata)
        {
            if (metadata?.AuthorizationServers == null) return false;
            
            foreach (var server in metadata.AuthorizationServers)
            {
                if (server.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task<string?> AcquireTokenAsync(string scope, AuthMetadata metadata, bool isInteractive, CancellationToken ct, string? tenantId = null, string? clientId = null)
        {
            // Try to get token silently first
            var token = await RunCliCommandAsync("gh", "auth token", ct);
            
            if (string.IsNullOrWhiteSpace(token) && isInteractive)
            {
                _logger.LogInformation("No active GitHub session found. Launching interactive login...");
                
                // Run interactive login
                var loginResult = await RunCliCommandAsync("gh", "auth login --web", ct, isInteractive: true);
                
                if (loginResult != null)
                {
                    // Try getting token again after login
                    token = await RunCliCommandAsync("gh", "auth token", ct);
                }
            }

            return token?.Trim();
        }
    }
}
