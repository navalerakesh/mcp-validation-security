using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the configuration for MCP server validation operations.
/// This class contains all necessary settings to perform comprehensive compliance testing.
/// </summary>
public class McpValidatorConfiguration
{
    /// <summary>
    /// Gets or sets the target MCP server configuration details.
    /// </summary>
    [JsonPropertyName("server")]
    public McpServerConfig Server { get; set; } = new();

    /// <summary>
    /// Gets or sets the validation scenarios to execute.
    /// </summary>
    [JsonPropertyName("validation")]
    public ValidationConfig Validation { get; set; } = new();

    /// <summary>
    /// Gets or sets the reporting configuration for test results.
    /// </summary>
    [JsonPropertyName("reporting")]
    public ReportingConfig Reporting { get; set; } = new();

    /// <summary>
    /// Gets or sets the CI/host policy used to translate validation results into pass/fail outcomes.
    /// </summary>
    [JsonPropertyName("policy")]
    public ValidationPolicyConfig Policy { get; set; } = new();

    /// <summary>
    /// Gets or sets the test execution settings.
    /// </summary>
    [JsonPropertyName("testExecution")]
    public TestExecutionConfig TestExecution { get; set; } = new();

    /// <summary>
    /// Gets or sets the optional client profile selection used for host-side compatibility evaluation.
    /// </summary>
    [JsonPropertyName("clientProfiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientProfileOptions? ClientProfiles { get; set; }

    /// <summary>
    /// Creates a copy of this validator configuration with server configuration
    /// cloned and secrets redacted for safe persistence.
    /// </summary>
    public McpValidatorConfiguration CloneWithoutSecrets()
    {
        return new McpValidatorConfiguration
        {
            Server = Server.CloneWithoutSecrets(),
            Validation = Validation,
            Reporting = Reporting,
            Policy = Policy,
            TestExecution = TestExecution,
            ClientProfiles = ClientProfiles
        };
    }
}

/// <summary>
/// Configuration for host-level pass/fail policy decisions.
/// This is intentionally separate from validation logic so different hosts can
/// apply the same validation evidence with different enforcement levels.
/// </summary>
public class ValidationPolicyConfig
{
    /// <summary>
    /// Gets or sets the policy mode. Supported values: advisory, balanced, strict.
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ValidationPolicyModes.Balanced;

    /// <summary>
    /// Gets or sets suppression entries applied only at the policy layer.
    /// Raw validation findings remain unchanged; suppressions only affect the final gate decision.
    /// </summary>
    [JsonPropertyName("suppressions")]
    public List<ValidationPolicySuppression> Suppressions { get; set; } = new();
}

/// <summary>
/// A host-level suppression entry used to mute specific policy signals without altering raw findings.
/// </summary>
public class ValidationPolicySuppression
{
    /// <summary>
    /// Optional identifier for the suppression entry.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Stable policy signal identifier to match, e.g. POLICY.TRUST.L3_MINIMUM.
    /// </summary>
    [JsonPropertyName("signalId")]
    public string? SignalId { get; set; }

    /// <summary>
    /// Stable rule identifier to match, e.g. MCP.TOOL.CALL.CONTENT_ARRAY_MISSING.
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string? RuleId { get; set; }

    /// <summary>
    /// Optional component selector, such as a tool/resource/prompt name.
    /// </summary>
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    /// <summary>
    /// Optional rule-source selector: spec, guideline, or heuristic.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Optional category selector.
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Owner responsible for the suppression.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Reason the suppression exists.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Expiry timestamp in UTC. Expired suppressions are ignored automatically.
    /// </summary>
    [JsonPropertyName("expiresOn")]
    public DateTimeOffset? ExpiresOn { get; set; }
}

/// <summary>
/// Stable names for validation policy modes.
/// </summary>
public static class ValidationPolicyModes
{
    public const string Advisory = "advisory";
    public const string Balanced = "balanced";
    public const string Strict = "strict";
}

/// <summary>
/// Defines the configuration for the target MCP server under test.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Gets or sets the server endpoint URL or connection string.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the transport mechanism (stdio, http, websocket).
    /// </summary>
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Gets or sets the MCP protocol version to use when validating this server.
    ///
    /// If not explicitly provided, this value is typically populated from the
    /// version negotiated during the MCP <c>initialize</c> handshake.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the authentication configuration if required.
    /// </summary>
    [JsonPropertyName("authentication")]
    public AuthenticationConfig? Authentication { get; set; }

    /// <summary>
    /// Gets or sets the connection timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 120000;

    /// <summary>
    /// Gets or sets additional headers for HTTP transport.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Gets or sets environment variables for stdio transport.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// Gets or sets the declared or inferred server profile so validators understand intent.
    /// </summary>
    [JsonPropertyName("profile")]
    public McpServerProfile Profile { get; set; } = McpServerProfile.Unspecified;

    /// <summary>
    /// Creates a copy of this configuration with secrets (tokens, passwords and sensitive headers) redacted
    /// for safe persistence to disk or logs.
    /// </summary>
    public McpServerConfig CloneWithoutSecrets()
    {
        var clone = new McpServerConfig
        {
            Endpoint = Endpoint,
            Transport = Transport,
            ProtocolVersion = ProtocolVersion,
            Profile = Profile,
            Authentication = Authentication?.CloneWithoutSecrets(),
            TimeoutMs = TimeoutMs,
            Headers = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(Environment)
        };

        foreach (var header in Headers)
        {
            clone.Headers[header.Key] = IsSensitiveHeaderKey(header.Key)
                ? "__HEADER-REDACTED__"
                : header.Value;
        }

        return clone;
    }

    internal static bool IsSensitiveHeaderKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var lower = key.ToLowerInvariant();

        return lower.Contains("authorization") ||
               lower.Contains("auth") ||
               lower.Contains("token") ||
               lower.Contains("secret") ||
               lower.Contains("api-key") ||
               lower.Contains("apikey");
    }
}

/// <summary>
/// Defines authentication configuration for MCP server connections.
/// </summary>
public class AuthenticationConfig
{
    /// <summary>
    /// Gets or sets the authentication type (bearer, basic, custom).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    /// <summary>
    /// Gets or sets whether authentication is required.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;

    /// <summary>
    /// Gets or sets the authentication token or credential.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the username for basic authentication.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password for basic authentication.
    /// </summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the Client ID for OAuth flows.
    /// </summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the Tenant ID for OAuth flows.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the scopes for OAuth flows.
    /// </summary>
    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    /// <summary>
    /// Gets or sets the authority URL for OAuth flows.
    /// </summary>
    [JsonPropertyName("authority")]
    public string? Authority { get; set; }

    /// <summary>
    /// Gets or sets custom authentication headers.
    /// </summary>
    [JsonPropertyName("customHeaders")]
    public Dictionary<string, string> CustomHeaders { get; set; } = new();

    /// <summary>
    /// Gets or sets whether interactive authentication is allowed.
    /// </summary>
    [JsonPropertyName("allowInteractive")]
    public bool AllowInteractive { get; set; } = false;

    /// <summary>
    /// Creates a copy of this authentication configuration with sensitive values redacted
    /// so they can be safely written to disk or logs.
    /// </summary>
    public AuthenticationConfig CloneWithoutSecrets()
    {
        var clone = new AuthenticationConfig
        {
            Type = Type,
            Required = Required,
            // Replace any real token value with an explicit redaction marker
            Token = string.IsNullOrEmpty(Token) ? null : "__TOKEN-REDACTED__",
            Username = Username,
            // Never persist real passwords
            Password = string.IsNullOrEmpty(Password) ? null : "__SECRET-REDACTED__",
            ClientId = ClientId,
            TenantId = TenantId,
            Scopes = Scopes,
            Authority = Authority,
            CustomHeaders = new Dictionary<string, string>(),
            AllowInteractive = AllowInteractive
        };

        foreach (var header in CustomHeaders)
        {
            clone.CustomHeaders[header.Key] = McpServerConfig.IsSensitiveHeaderKey(header.Key)
                ? "__HEADER-REDACTED__"
                : header.Value;
        }

        return clone;
    }
}

/// <summary>
/// Represents the validation configuration structure that wraps categories.
/// </summary>
public class ValidationConfig
{
    /// <summary>
    /// Gets or sets the validation categories/scenarios.
    /// </summary>
    [JsonPropertyName("categories")]
    public ValidationScenarios Categories { get; set; } = new();
}
