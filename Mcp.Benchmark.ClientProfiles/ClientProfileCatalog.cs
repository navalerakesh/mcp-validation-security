using Mcp.Benchmark.Core.Models;
namespace Mcp.Benchmark.ClientProfiles;

public static class ClientProfileCatalog
{
    public const string AllProfilesToken = "all";

    private static readonly IReadOnlyList<ClientProfileDescriptor> Profiles = new[]
    {
        new ClientProfileDescriptor
        {
            Id = "claude-code",
            DisplayName = "Claude Code",
            Revision = "2026-04",
            DocumentationUrl = "https://code.claude.com/docs/en/mcp"
        },
        new ClientProfileDescriptor
        {
            Id = "vscode-copilot-agent",
            DisplayName = "VS Code Copilot Agent",
            Revision = "2026-04",
            DocumentationUrl = "https://code.visualstudio.com/docs/copilot/chat/mcp-servers"
        },
        new ClientProfileDescriptor
        {
            Id = "github-copilot-cli",
            DisplayName = "GitHub Copilot CLI",
            Revision = "2026-04",
            DocumentationUrl = "https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers"
        },
        new ClientProfileDescriptor
        {
            Id = "github-copilot-cloud-agent",
            DisplayName = "GitHub Copilot Cloud Agent",
            Revision = "2026-04",
            DocumentationUrl = "https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp"
        },
        new ClientProfileDescriptor
        {
            Id = "visual-studio-copilot",
            DisplayName = "Visual Studio Copilot",
            Revision = "2026-04",
            DocumentationUrl = "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022"
        }
    };

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = "claude-code",
        ["vscode"] = "vscode-copilot-agent",
        ["vscode-copilot"] = "vscode-copilot-agent",
        ["copilot-cli"] = "github-copilot-cli",
        ["cloud-agent"] = "github-copilot-cloud-agent",
        ["copilot-cloud-agent"] = "github-copilot-cloud-agent",
        ["visual-studio"] = "visual-studio-copilot",
        ["vs-copilot"] = "visual-studio-copilot"
    };

    public static IReadOnlyList<ClientProfileDescriptor> SupportedProfiles => Profiles;

    public static IReadOnlyList<string> SupportedProfileIds => Profiles.Select(profile => profile.Id).ToArray();

    public static string ResolveCanonicalProfileId(string requestedProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedProfileId);

        if (Profiles.Any(profile => string.Equals(profile.Id, requestedProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            return Profiles.First(profile => string.Equals(profile.Id, requestedProfileId, StringComparison.OrdinalIgnoreCase)).Id;
        }

        if (Aliases.TryGetValue(requestedProfileId, out var canonicalId))
        {
            return canonicalId;
        }

        throw new ArgumentException(
            $"Unknown client profile '{requestedProfileId}'. Supported profiles: {string.Join(", ", SupportedProfileIds)}.",
            nameof(requestedProfileId));
    }

    public static ClientProfileDescriptor GetDescriptor(string canonicalProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalProfileId);

        return Profiles.First(profile => string.Equals(profile.Id, canonicalProfileId, StringComparison.OrdinalIgnoreCase));
    }
}