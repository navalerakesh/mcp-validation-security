using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Benchmark.Core.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Authentication
{
    public interface IAuthenticationService
    {
        Task<string?> GetTokenAsync(AuthMetadata metadata, CancellationToken ct, bool isInteractive = true, string? tenantId = null, string? clientId = null);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly IEnumerable<IAuthenticationStrategy> _strategies;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            IEnumerable<IAuthenticationStrategy> strategies,
            ILogger<AuthenticationService> logger)
        {
            _strategies = strategies;
            _logger = logger;
        }

        public async Task<string?> GetTokenAsync(AuthMetadata metadata, CancellationToken ct, bool isInteractive = true, string? tenantId = null, string? clientId = null)
        {
            if (metadata == null)
            {
                _logger.LogWarning("Authentication metadata is null");
                return null;
            }

            var strategy = _strategies.FirstOrDefault(s => s.CanHandle(metadata));
            
            if (strategy == null)
            {
                _logger.LogWarning("No authentication strategy found for the provided metadata");
                return null;
            }

            _logger.LogInformation($"Using authentication strategy: {strategy.ProviderName}");
            
            var scope = metadata.ScopesSupported?.FirstOrDefault() ?? "default";
            return await strategy.AcquireTokenAsync(scope, metadata, isInteractive, ct, tenantId, clientId);
        }
    }
}
