using Mcp.Benchmark.Core.Resources;
using System.Security.Cryptography;
using System.Text;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Professional authentication token generator for MCP validation testing.
/// Generates realistic-looking tokens that follow industry authentication patterns.
/// These tokens are for testing purposes only and should not be used in production.
/// 
/// Reference: https://modelcontextprotocol.io/specification/basic/authentication
/// </summary>
public interface IAuthenticationTokenGenerator
{
    /// <summary>
    /// Generates a realistic Bearer token for testing authentication flows.
    /// </summary>
    /// <param name="tokenType">Type of token to generate</param>
    /// <returns>Realistic authentication token string</returns>
    string GenerateTestBearerToken(TokenType tokenType = TokenType.Standard);

    /// <summary>
    /// Generates a realistic API key for testing authentication flows.
    /// </summary>
    /// <param name="prefix">Optional prefix for the API key</param>
    /// <returns>Realistic API key string</returns>
    string GenerateTestApiKey(string? prefix = null);

    /// <summary>
    /// Generates realistic HTTP Basic authentication credentials.
    /// </summary>
    /// <param name="username">Username for basic auth</param>
    /// <param name="password">Password for basic auth</param>
    /// <returns>Base64-encoded basic auth string</returns>
    string GenerateBasicAuthToken(string username = "test-user", string password = "test-password");

    /// <summary>
    /// Creates a properly formatted Authorization header value.
    /// </summary>
    /// <param name="scheme">Authentication scheme (Bearer, Basic, etc.)</param>
    /// <param name="token">Token or credentials</param>
    /// <returns>Formatted Authorization header value</returns>
    string CreateAuthorizationHeader(string scheme, string token);
}

/// <summary>
/// Types of authentication tokens that can be generated for testing.
/// </summary>
public enum TokenType
{
    /// <summary>Standard JWT-like token</summary>
    Standard,
    /// <summary>GitHub-style personal access token</summary>
    GitHub,
    /// <summary>OpenAI-style API key</summary>
    OpenAI,
    /// <summary>Custom format token</summary>
    Custom
}

/// <summary>
/// Implementation of professional authentication token generator.
/// Creates realistic tokens that follow real-world authentication patterns.
/// </summary>
public class AuthenticationTokenGenerator : IAuthenticationTokenGenerator
{
    private readonly Random _random = new(Environment.TickCount);

    public string GenerateTestBearerToken(TokenType tokenType = TokenType.Standard)
    {
        return tokenType switch
        {
            TokenType.Standard => GenerateJwtStyleToken(),
            TokenType.GitHub => GenerateGitHubStyleToken(),
            TokenType.OpenAI => GenerateOpenAIStyleToken(),
            TokenType.Custom => GenerateCustomToken(),
            _ => GenerateJwtStyleToken()
        };
    }

    public string GenerateTestApiKey(string? prefix = null)
    {
        var keyPrefix = prefix ?? "mcp_test";
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var keyBody = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        
        return $"{keyPrefix}_{keyBody}";
    }

    public string GenerateBasicAuthToken(string username = "test-user", string password = "test-password")
    {
        var credentials = $"{username}:{password}";
        var credentialsBytes = Encoding.UTF8.GetBytes(credentials);
        return Convert.ToBase64String(credentialsBytes);
    }

    public string CreateAuthorizationHeader(string scheme, string token)
    {
        return $"{scheme} {token}";
    }

    /// <summary>
    /// Generates a JWT-style token with realistic structure.
    /// Format: header.payload.signature (base64url encoded)
    /// </summary>
    private string GenerateJwtStyleToken()
    {
        // Realistic JWT header
        var header = new
        {
            alg = "HS256",
            typ = "JWT"
        };

        // Realistic JWT payload
        var payload = new
        {
            sub = "mcp-validator-test",
            aud = "mcp-server",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            scope = "tools:read tools:execute resources:read"
        };

        var headerBase64 = EncodeBase64Url(System.Text.Json.JsonSerializer.Serialize(header));
        var payloadBase64 = EncodeBase64Url(System.Text.Json.JsonSerializer.Serialize(payload));
        
        // Generate realistic signature (for testing only)
        var signatureBytes = new byte[32];
        RandomNumberGenerator.Fill(signatureBytes);
        var signatureBase64 = EncodeBase64Url(signatureBytes);

        return $"{headerBase64}.{payloadBase64}.{signatureBase64}";
    }

    /// <summary>
    /// Generates a GitHub-style personal access token.
    /// Format: ghp_[40 character string]
    /// </summary>
    private string GenerateGitHubStyleToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var tokenChars = new char[40];
        
        for (int i = 0; i < 40; i++)
        {
            tokenChars[i] = chars[_random.Next(chars.Length)];
        }

        return $"ghp_{new string(tokenChars)}";
    }

    /// <summary>
    /// Generates an OpenAI-style API key.
    /// Format: sk-[48 character string]
    /// </summary>
    private string GenerateOpenAIStyleToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var tokenChars = new char[48];
        
        for (int i = 0; i < 48; i++)
        {
            tokenChars[i] = chars[_random.Next(chars.Length)];
        }

        return $"sk-{new string(tokenChars)}";
    }

    /// <summary>
    /// Generates a custom format token for MCP testing.
    /// Format: mcp_[timestamp]_[32 character string]
    /// </summary>
    private string GenerateCustomToken()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var randomBytes = new byte[24];
        RandomNumberGenerator.Fill(randomBytes);
        var randomString = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"mcp_{timestamp}_{randomString}";
    }

    /// <summary>
    /// Encodes data to base64url format (RFC 4648).
    /// </summary>
    private static string EncodeBase64Url(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return EncodeBase64Url(bytes);
    }

    /// <summary>
    /// Encodes bytes to base64url format (RFC 4648).
    /// </summary>
    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
