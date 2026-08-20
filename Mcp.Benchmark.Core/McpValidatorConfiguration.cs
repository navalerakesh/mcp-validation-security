using System.Text.Json.Serialization;
using System.Text.Json;

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
    /// Gets or sets the execution governance policy for the run.
    /// </summary>
    [JsonPropertyName("execution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionPolicy? Execution { get; set; } = new();

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
    /// Gets or sets optional advisory evaluation overlays.
    /// </summary>
    [JsonPropertyName("evaluation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvaluationPolicy? Evaluation { get; set; } = new();

    /// <summary>
    /// Creates a copy of this validator configuration with server configuration
    /// cloned and secrets redacted for safe persistence.
    /// </summary>
    public McpValidatorConfiguration CloneWithoutSecrets()
    {
        var redacted = new McpValidatorConfiguration
        {
            Server = Server.CloneWithoutSecrets(),
            Validation = Validation,
            Reporting = Reporting,
            Policy = Policy,
            Execution = Execution?.Clone(),
            TestExecution = TestExecution,
            ClientProfiles = ClientProfiles,
            Evaluation = Evaluation?.Clone()
        };
        return JsonSerializer.Deserialize<McpValidatorConfiguration>(JsonSerializer.Serialize(redacted))
            ?? throw new InvalidOperationException("Unable to clone validator configuration.");
    }

    /// <summary>
    /// Creates a clone suitable for deterministic result persistence.
    /// Execution governance and experimental evaluation overlays remain operational-only.
    /// </summary>
    public McpValidatorConfiguration CloneForDeterministicResult()
    {
        var clone = CloneWithoutSecrets();
        clone.Execution = null;
        clone.Evaluation = null;
        clone.Reporting.OutputDirectory = null;
        return clone;
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

    /// <summary>
    /// Gets or sets whether policy blocks only regressions relative to an explicit baseline result.
    /// Execution-integrity failures always remain blocking.
    /// </summary>
    [JsonPropertyName("regressionOnly")]
    public bool RegressionOnly { get; set; }
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
    /// Gets or sets whether protocol negotiation is automatic or constrained to a legacy or modern era.
    /// </summary>
    [JsonPropertyName("protocolEra")]
    public McpProtocolEraSelection ProtocolEra { get; set; } = McpProtocolEraSelection.Auto;

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
    /// Gets or sets the operating context used to calibrate static content safety risk.
    /// </summary>
    [JsonPropertyName("contentSafetyContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ContentSafetyContextProfile ContentSafetyContext { get; set; } = ContentSafetyContextProfile.Unspecified;

    public McpServerConfig CloneForExecution()
    {
        return new McpServerConfig
        {
            Endpoint = Endpoint,
            Transport = Transport,
            ProtocolVersion = ProtocolVersion,
            ProtocolEra = ProtocolEra,
            Profile = Profile,
            ContentSafetyContext = ContentSafetyContext,
            Authentication = Authentication?.CloneForExecution(),
            TimeoutMs = TimeoutMs,
            Headers = new Dictionary<string, string>(Headers),
            Environment = new Dictionary<string, string>(Environment)
        };
    }

    /// <summary>
    /// Creates a copy of this configuration with secrets (tokens, passwords and sensitive headers) redacted
    /// for safe persistence to disk or logs.
    /// </summary>
    public McpServerConfig CloneWithoutSecrets()
    {
        var clone = new McpServerConfig
        {
            Endpoint = !string.IsNullOrWhiteSpace(Endpoint) && string.Equals(Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                ? "__STDIO_COMMAND_REDACTED__"
                : RedactEndpoint(Endpoint),
            Transport = Transport,
            ProtocolVersion = ProtocolVersion,
            ProtocolEra = ProtocolEra,
            Profile = Profile,
            ContentSafetyContext = ContentSafetyContext,
            Authentication = Authentication?.CloneWithoutSecrets(),
            TimeoutMs = TimeoutMs,
            Headers = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>()
        };

        foreach (var header in Headers)
        {
            clone.Headers[header.Key] = IsSensitiveHeaderKey(header.Key)
                ? "__HEADER-REDACTED__"
                : header.Value;
        }

        foreach (var variable in Environment)
        {
            clone.Environment[variable.Key] = IsSensitiveEnvironmentKey(variable.Key)
                ? "__ENVIRONMENT-REDACTED__"
                : variable.Value;
        }

        return clone;
    }

    private static string? RedactEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "ws" or "wss"))
        {
            return endpoint;
        }

        return new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri;
    }

    internal static bool IsSensitiveEnvironmentKey(string key)
    {
        return IsSensitiveHeaderKey(key) ||
               key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("private", StringComparison.OrdinalIgnoreCase);
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
    /// Gets or sets a transient credential reference resolved by the runtime host.
    /// Direct token values remain supported for 1.x compatibility and take precedence.
    /// </summary>
    [JsonPropertyName("tokenRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecretRef? TokenRef { get; set; }

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

    [JsonPropertyName("clientRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OAuthClientRegistrationConfig? ClientRegistration { get; set; }

    [JsonPropertyName("conformanceCredentials")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthenticationConformanceCredentials? ConformanceCredentials { get; set; }

    public AuthenticationConfig CloneForExecution()
    {
        return new AuthenticationConfig
        {
            Type = Type,
            Required = Required,
            Token = Token,
            TokenRef = TokenRef?.Clone(),
            Username = Username,
            Password = Password,
            ClientId = ClientId,
            TenantId = TenantId,
            Scopes = Scopes?.ToArray(),
            Authority = Authority,
            CustomHeaders = new Dictionary<string, string>(CustomHeaders),
            AllowInteractive = AllowInteractive,
            ClientRegistration = ClientRegistration?.Clone(),
            ConformanceCredentials = ConformanceCredentials?.Clone()
        };
    }

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
            TokenRef = TokenRef?.Clone(),
            Username = Username,
            // Never persist real passwords
            Password = string.IsNullOrEmpty(Password) ? null : "__SECRET-REDACTED__",
            ClientId = ClientId,
            TenantId = TenantId,
            Scopes = Scopes?.ToArray(),
            Authority = Authority,
            CustomHeaders = new Dictionary<string, string>(),
            AllowInteractive = AllowInteractive,
            ClientRegistration = ClientRegistration?.Clone(),
            ConformanceCredentials = ConformanceCredentials?.Clone()
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

public sealed class SecretRef
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = SecretRefProviders.Environment;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    public SecretRef Clone() => new() { Provider = Provider, Name = Name };
}

public static class SecretRefProviders
{
    public const string Environment = "environment";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OAuthClientRegistrationMode
{
    None,
    Static,
    MetadataDocument,
    LegacyDynamic
}

public sealed class OAuthClientRegistrationConfig
{
    [JsonPropertyName("mode")]
    public OAuthClientRegistrationMode Mode { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("metadataUri")]
    public string? MetadataUri { get; set; }

    [JsonPropertyName("authorizationServerIssuer")]
    public string? AuthorizationServerIssuer { get; set; }

    [JsonPropertyName("redirectUris")]
    public List<string> RedirectUris { get; set; } = new();

    public OAuthClientRegistrationConfig Clone() => new()
    {
        Mode = Mode,
        ClientId = ClientId,
        MetadataUri = MetadataUri,
        AuthorizationServerIssuer = AuthorizationServerIssuer,
        RedirectUris = new List<string>(RedirectUris)
    };
}

public sealed class AuthenticationConformanceCredentials
{
    [JsonPropertyName("wrongAudienceResource")]
    public string? WrongAudienceResource { get; set; }

    [JsonPropertyName("wrongAudienceScopes")]
    public List<string> WrongAudienceScopes { get; set; } = new();

    [JsonPropertyName("previouslyGrantedScopes")]
    public List<string> PreviouslyGrantedScopes { get; set; } = new();

    public AuthenticationConformanceCredentials Clone() => new()
    {
        WrongAudienceResource = WrongAudienceResource,
        WrongAudienceScopes = new List<string>(WrongAudienceScopes),
        PreviouslyGrantedScopes = new List<string>(PreviouslyGrantedScopes)
    };
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
