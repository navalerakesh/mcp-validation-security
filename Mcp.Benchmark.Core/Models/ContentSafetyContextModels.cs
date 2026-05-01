namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Declares the operating context used to calibrate metadata-only content safety findings.
/// </summary>
public enum ContentSafetyContextProfile
{
    Unspecified = 0,
    PublicUnauthenticated,
    PublicAuthenticated,
    EnterpriseGoverned,
    LocalDeveloper,
    CIOnly,
    Internal
}

/// <summary>
/// Captures contextual information that changes how static content safety findings should be interpreted.
/// </summary>
public sealed class ContentSafetyAnalysisContext
{
    public ContentSafetyContextProfile Profile { get; init; } = ContentSafetyContextProfile.Unspecified;

    public McpServerProfile ServerProfile { get; init; } = McpServerProfile.Unspecified;

    public bool AuthenticationRequired { get; init; }

    public IReadOnlyList<AiSafetyControlEvidence> ObservedControls { get; init; } = Array.Empty<AiSafetyControlEvidence>();

    public static ContentSafetyAnalysisContext FromServerConfig(
        McpServerConfig? serverConfig,
        IEnumerable<AiSafetyControlEvidence>? observedControls = null)
    {
        var controlEvidence = observedControls?.ToArray() ?? Array.Empty<AiSafetyControlEvidence>();
        return new ContentSafetyAnalysisContext
        {
            Profile = ResolveProfile(serverConfig),
            ServerProfile = serverConfig?.Profile ?? McpServerProfile.Unspecified,
            AuthenticationRequired = IsAuthenticationRequired(serverConfig),
            ObservedControls = controlEvidence
        };
    }

    private static ContentSafetyContextProfile ResolveProfile(McpServerConfig? serverConfig)
    {
        if (serverConfig == null)
        {
            return ContentSafetyContextProfile.Unspecified;
        }

        if (serverConfig.ContentSafetyContext != ContentSafetyContextProfile.Unspecified)
        {
            return serverConfig.ContentSafetyContext;
        }

        return serverConfig.Profile switch
        {
            McpServerProfile.Public => ContentSafetyContextProfile.PublicUnauthenticated,
            McpServerProfile.Authenticated => ContentSafetyContextProfile.PublicAuthenticated,
            McpServerProfile.Enterprise => ContentSafetyContextProfile.EnterpriseGoverned,
            _ when IsLocalDeveloperEndpoint(serverConfig) => ContentSafetyContextProfile.LocalDeveloper,
            _ => ContentSafetyContextProfile.Unspecified
        };
    }

    private static bool IsAuthenticationRequired(McpServerConfig? serverConfig)
    {
        if (serverConfig == null)
        {
            return false;
        }

        return serverConfig.Profile is McpServerProfile.Authenticated or McpServerProfile.Enterprise ||
               serverConfig.Authentication?.Required == true ||
               !string.IsNullOrWhiteSpace(serverConfig.Authentication?.Token);
    }

    private static bool IsLocalDeveloperEndpoint(McpServerConfig serverConfig)
    {
        if (string.Equals(serverConfig.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(serverConfig.Endpoint, UriKind.Absolute, out var endpointUri) && endpointUri.IsLoopback;
    }
}
