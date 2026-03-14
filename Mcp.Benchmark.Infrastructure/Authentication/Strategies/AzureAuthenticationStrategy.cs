using System;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Authentication.Strategies
{
    public class AzureAuthenticationStrategy : BaseCliStrategy, IAuthenticationStrategy
    {
        public string ProviderName => "Azure";

        public AzureAuthenticationStrategy(ILogger<AzureAuthenticationStrategy> logger) : base(logger)
        {
        }

        public bool CanHandle(AuthMetadata metadata)
        {
            if (metadata?.AuthorizationServers == null) return false;
            
            foreach (var server in metadata.AuthorizationServers)
            {
                if (server.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task<string?> AcquireTokenAsync(string scope, AuthMetadata metadata, bool isInteractive, CancellationToken ct, string? tenantId = null, string? clientId = null)
        {
            // If tenantId is not explicitly provided, try to extract it from metadata
            if (string.IsNullOrEmpty(tenantId) && metadata?.AuthorizationServers?.Any() == true)
            {
                // Try to extract tenant ID from the first auth server URL
                // Format: https://login.microsoftonline.com/{tenantId}/v2.0
                var authUrl = metadata.AuthorizationServers.First();
                var parts = authUrl.Split('/');
                if (parts.Length > 3 && parts[2].Contains("login.microsoftonline.com"))
                {
                    tenantId = parts[3];
                }
            }

            string cmdArgs = $"account get-access-token --scope \"{scope}\" --query accessToken -o tsv";
            if (!string.IsNullOrEmpty(tenantId))
            {
                cmdArgs += $" --tenant {tenantId}";
            }

            // Try to get token silently first
            var token = await RunCliCommandAsync("az", cmdArgs, ct);
            
            if (string.IsNullOrWhiteSpace(token) && isInteractive)
            {
                _logger.LogInformation("No active Azure session found. Launching interactive login...");
                
                // Run interactive login
                var loginResult = await RunCliCommandAsync("az", "login", ct, isInteractive: true);
                
                if (loginResult != null)
                {
                    // Try getting token again after login
                    token = await RunCliCommandAsync("az", cmdArgs, ct);
                }
            }

            return token?.Trim();
        }
    }
}
