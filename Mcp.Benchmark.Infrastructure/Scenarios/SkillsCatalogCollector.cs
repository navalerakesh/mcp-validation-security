using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

internal static class SkillsCatalogCollector
{
    private const int MaximumPages = 50;

    public static async Task<SkillsCatalogCollection> CollectAsync(
        IMcpHttpClient httpClient,
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        var pages = new List<string>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        try
        {
            do
            {
                var parameters = cursor == null ? null : new { cursor };
                var response = await httpClient.CallAsync(
                    serverConfig.Endpoint!,
                    McpSpecConstants.ExtensionMethods.SkillsList,
                    parameters,
                    serverConfig.Authentication,
                    cancellationToken);
                if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
                {
                    return SkillsCatalogCollection.Unavailable(
                        response.Error ?? "skills/list did not return a successful response.");
                }

                pages.Add(response.RawJson);
                cursor = SkillsCatalogAnalyzer.ReadNextCursor(response.RawJson);
                if (cursor != null && !cursors.Add(cursor))
                {
                    return SkillsCatalogCollection.Inconclusive("skills/list pagination repeated a cursor.");
                }
            }
            while (cursor != null && pages.Count < MaximumPages);

            return cursor == null
                ? SkillsCatalogCollection.Complete(pages)
                : SkillsCatalogCollection.Inconclusive($"skills/list exceeded the bounded {MaximumPages}-page catalog limit.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SkillsCatalogCollection.Unavailable(
                "skills/list could not be collected under the active execution policy.");
        }
    }
}

internal sealed record SkillsCatalogCollection(
    bool IsComplete,
    bool IsUnavailable,
    string? Reason,
    IReadOnlyList<string> Pages)
{
    public static SkillsCatalogCollection Complete(IReadOnlyList<string> pages) => new(true, false, null, pages);

    public static SkillsCatalogCollection Inconclusive(string reason) => new(false, false, reason, Array.Empty<string>());

    public static SkillsCatalogCollection Unavailable(string reason) => new(false, true, reason, Array.Empty<string>());
}
