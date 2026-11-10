using System.Runtime.CompilerServices;
using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class BoundedCatalogReader
{
    internal static async IAsyncEnumerable<JsonElement> ReadAsync(JsonRpcResponse initialResponse, string collection,
        int maximumItems, CatalogEnumerationEvidence evidence, IList<string> issues,
        Func<string, CancellationToken, Task<JsonRpcResponse>> fetchPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = initialResponse;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var processed = 0;
        long payloadCharacters = 0;
        maximumItems = Math.Clamp(maximumItems, 1, CatalogEnumerationLimits.MaximumItems);
        evidence.PageAttempts = 1;

        void CheckCancellation()
        {
            if (!cancellationToken.IsCancellationRequested) return;
            evidence.Termination = CatalogEnumerationTermination.Cancelled;
            cancellationToken.ThrowIfCancellationRequested();
        }

        while (true)
        {
            CheckCancellation();
            if (response is not null && AuthenticationChallengeInterpreter.Inspect(response).RequiresAuthentication)
            {
                evidence.Termination = CatalogEnumerationTermination.AuthenticationRequired;
                issues.Add("Catalog continuation requires authentication; earlier metadata remains available.");
                yield break;
            }
            if (response is null || !response.IsSuccess)
            {
                evidence.Termination = CatalogEnumerationTermination.TransportFailure;
                issues.Add($"Catalog page {evidence.PageAttempts} failed; earlier metadata remains available.");
                yield break;
            }
            var raw = response.RawJson;
            payloadCharacters += raw?.Length ?? 0;
            evidence.PayloadCharactersObserved = payloadCharacters;
            if (payloadCharacters > CatalogEnumerationLimits.MaximumPayloadCharacters)
            {
                evidence.Termination = CatalogEnumerationTermination.BudgetExceeded;
                issues.Add("Catalog payload exceeded the retained-character budget.");
                yield break;
            }
            JsonDocument? document = null;
            try
            {
                if (!string.IsNullOrEmpty(raw)) document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 64 });
            }
            catch (JsonException)
            {
                document = null;
            }
            if (document is null)
            {
                evidence.RejectedPages++;
                evidence.Termination = CatalogEnumerationTermination.MalformedPage;
                issues.Add("Catalog page has no valid bounded JSON payload.");
                yield break;
            }
            string? cursor = null;
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("result", out var result) ||
                    result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(collection, out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    evidence.RejectedPages++;
                    evidence.Termination = CatalogEnumerationTermination.MalformedPage;
                    issues.Add("Catalog page does not contain the expected result array.");
                    yield break;
                }
                evidence.ParsedPages++;
                evidence.RawItemsObserved += items.GetArrayLength();
                var invalidCursor = false;
                if (result.TryGetProperty("nextCursor", out var next))
                {
                    invalidCursor = next.ValueKind != JsonValueKind.String;
                    if (!invalidCursor)
                    {
                        cursor = next.GetString();
                        evidence.ContinuationObserved = true;
                    }
                }
                foreach (var item in items.EnumerateArray())
                {
                    CheckCancellation();
                    if (processed >= maximumItems)
                    {
                        evidence.Termination = CatalogEnumerationTermination.BudgetExceeded;
                        issues.Add("Catalog processing stopped at its item budget.");
                        yield break;
                    }
                    processed++;
                    yield return item;
                }
                if (invalidCursor)
                {
                    evidence.RejectedPages++;
                    evidence.Termination = CatalogEnumerationTermination.MalformedPage;
                    issues.Add("Catalog continuation cursor must be an opaque string.");
                    yield break;
                }
            }
            CheckCancellation();
            if (cursor is null)
            {
                evidence.Termination = CatalogEnumerationTermination.Complete;
                evidence.IsComplete = evidence.RejectedItems == 0 && evidence.RejectedPages == 0;
                yield break;
            }
            if (cursor.Length > CatalogEnumerationLimits.MaximumCursorCharacters || processed >= maximumItems ||
                evidence.PageAttempts >= CatalogEnumerationLimits.MaximumPages)
            {
                evidence.Termination = CatalogEnumerationTermination.BudgetExceeded;
                issues.Add("Catalog continuation stopped at its bounded page, item or cursor limit.");
                yield break;
            }
            if (!cursors.Add(cursor))
            {
                evidence.Termination = CatalogEnumerationTermination.CursorLoop;
                issues.Add("Catalog continuation repeated cursor; opaque value omitted.");
                yield break;
            }
            evidence.PageAttempts++;
            try
            {
                response = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                evidence.Termination = CatalogEnumerationTermination.Cancelled;
                throw;
            }
        }
    }
}