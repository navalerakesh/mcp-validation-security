using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class McpContentValidationUtils
{
    public static bool IsValidBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            Span<byte> buffer = value.Length <= 8192 ? stackalloc byte[value.Length] : new byte[value.Length];
            return Convert.TryFromBase64String(value, buffer, out _);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ValidationFinding> ValidateAnnotations(
        JsonElement annotatedElement,
        string component,
        string ruleId,
        string category)
    {
        if (!annotatedElement.TryGetProperty("annotations", out var annotations))
        {
            return Array.Empty<ValidationFinding>();
        }

        var findings = new List<ValidationFinding>();
        if (annotations.ValueKind != JsonValueKind.Object)
        {
            findings.Add(CreateAnnotationFinding(
                ruleId,
                category,
                component,
                "annotations",
                "Annotations must be an object when present.",
                "Return annotations as an object containing valid audience, priority, or lastModified fields."));
            return findings;
        }

        if (annotations.TryGetProperty("audience", out var audience))
        {
            var validAudience = audience.ValueKind == JsonValueKind.Array &&
                audience.EnumerateArray().All(value =>
                    value.ValueKind == JsonValueKind.String && value.GetString() is "user" or "assistant");

            if (!validAudience)
            {
                findings.Add(CreateAnnotationFinding(
                    ruleId,
                    category,
                    component,
                    "annotations.audience",
                    "annotations.audience must be an array containing only user and assistant values.",
                    "Set annotations.audience to values such as [\"user\"], [\"assistant\"], or [\"user\", \"assistant\"]."));
            }
        }

        if (annotations.TryGetProperty("priority", out var priority))
        {
            var validPriority = priority.ValueKind == JsonValueKind.Number &&
                priority.TryGetDouble(out var priorityValue) &&
                priorityValue >= 0.0 &&
                priorityValue <= 1.0;

            if (!validPriority)
            {
                findings.Add(CreateAnnotationFinding(
                    ruleId,
                    category,
                    component,
                    "annotations.priority",
                    "annotations.priority must be a number between 0.0 and 1.0.",
                    "Use a priority value in the inclusive range 0.0 to 1.0 so clients can safely rank context."));
            }
        }

        if (annotations.TryGetProperty("lastModified", out var lastModified))
        {
            var validLastModified = lastModified.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(lastModified.GetString(), out _);

            if (!validLastModified)
            {
                findings.Add(CreateAnnotationFinding(
                    ruleId,
                    category,
                    component,
                    "annotations.lastModified",
                    "annotations.lastModified must be an ISO 8601 timestamp string.",
                    "Use an ISO 8601 timestamp such as 2025-01-12T15:00:58Z for annotations.lastModified."));
            }
        }

        return findings;
    }

    private static ValidationFinding CreateAnnotationFinding(
        string ruleId,
        string category,
        string component,
        string field,
        string summary,
        string recommendation) => new()
        {
            RuleId = ruleId,
            Category = category,
            Component = component,
            Severity = ValidationFindingSeverity.Medium,
            Summary = summary,
            Recommendation = recommendation,
            Metadata =
            {
                ["field"] = field
            }
        };
}
