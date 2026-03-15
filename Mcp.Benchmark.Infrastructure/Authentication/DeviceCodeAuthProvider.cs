using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Authentication;

/// <summary>
/// Authentication provider that uses the OAuth 2.0 Device Code Flow.
/// Suitable for CLI applications where the user authenticates via a browser on another device.
/// </summary>
public class DeviceCodeAuthProvider : IAuthProvider
{
    private readonly string _clientId;
    private readonly string[] _scopes;
    private readonly string _tenantId;
    private readonly IPublicClientApplication _app;

    public string IdentityName { get; private set; }

    /// <summary>
    /// Initializes a new instance of the DeviceCodeAuthProvider class.
    /// </summary>
    /// <param name="clientId">The Client ID of the application.</param>
    /// <param name="scopes">The scopes to request access for.</param>
    /// <param name="identityName">A friendly name for this identity (e.g., "admin", "user").</param>
    /// <param name="tenantId">Optional Tenant ID (defaults to "common").</param>
    public DeviceCodeAuthProvider(string clientId, string[] scopes, string identityName, string tenantId = "common")
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        IdentityName = identityName ?? "User";
        _tenantId = tenantId;

        var builder = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
            .WithDefaultRedirectUri() // Use the default redirect URI for public clients
            .WithLogging((level, message, containsPii) =>
            {
                // Only log errors or warnings to avoid noise, unless debugging
                if (level == LogLevel.Error || level == LogLevel.Warning)
                {
                    Console.WriteLine($"[MSAL {level}] {message}");
                }
            }, LogLevel.Warning, enablePiiLogging: false, enableDefaultPlatformLogging: true);

        _app = builder.Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Attempt silent token acquisition first (check cache)
            var accounts = await _app.GetAccountsAsync();
            var result = await _app.AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                                   .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // Interactive device code flow
            Console.WriteLine("[Auth] Starting Device Code Flow...");
            
            var result = await _app.AcquireTokenWithDeviceCode(_scopes, deviceCodeCallback =>
            {
                Console.WriteLine();
                Console.WriteLine("----------------------------------------------------------------");
                Console.WriteLine(deviceCodeCallback.Message);
                Console.WriteLine("----------------------------------------------------------------");
                Console.WriteLine();
                return Task.CompletedTask;
            }).ExecuteAsync(cancellationToken);

            Console.WriteLine("[Auth] Token acquired successfully.");
            return result.AccessToken;
        }
    }
}
