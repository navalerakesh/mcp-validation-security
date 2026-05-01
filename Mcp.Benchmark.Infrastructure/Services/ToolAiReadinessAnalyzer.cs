using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services;

/// <summary>
/// Centralizes AI-readiness heuristics for tool schemas and error responses.
/// </summary>
public sealed class ToolAiReadinessAnalyzer : IToolAiReadinessAnalyzer
{
    public ToolAiReadinessAnalysis AnalyzeCatalog(IReadOnlyCollection<ToolAiReadinessTarget> tools, string? rawJson, long? totalPayloadChars = null)
    {
        if (tools.Count == 0)
        {
            return new ToolAiReadinessAnalysis();
        }

        double totalScore = 0;
        int scored = 0;
        var findings = new List<ValidationFinding>();

        foreach (var tool in tools)
        {
            double toolScore = 100.0;
            var schema = tool.InputSchema;
            var requiredProperties = GetRequiredPropertyNames(schema);

            if (schema.ValueKind == JsonValueKind.Undefined ||
                !schema.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object ||
                properties.EnumerateObject().Count() == 0)
            {
                totalScore += 80.0;
                scored++;
                continue;
            }

            int paramCount = 0;
            int undescribedParams = 0;
            int vagueStringParams = 0;
            int requiredArrayParamsMissingShape = 0;
            int enumCoverageMissingParams = 0;
            int formatHintMissingParams = 0;

            foreach (var property in properties.EnumerateObject())
            {
                paramCount++;
                var isRequired = requiredProperties.Contains(property.Name);

                if (!property.Value.TryGetProperty("description", out _))
                {
                    undescribedParams++;
                }

                if (property.Value.TryGetProperty("type", out var type) && type.GetString() == "string")
                {
                    var looksEnumerated = LooksLikeEnumeratedChoice(property.Name, property.Value);
                    var looksStructured = LooksLikeStructuredFormatValue(property.Name, property.Value);
                    var hasConstraint = property.Value.TryGetProperty("enum", out _) ||
                                        property.Value.TryGetProperty("pattern", out _) ||
                                        property.Value.TryGetProperty("format", out _);
                    var hasSemanticGuidance = HasSemanticGuidance(property.Value);

                    if (!hasConstraint && !hasSemanticGuidance && !looksEnumerated && !looksStructured)
                    {
                        vagueStringParams++;
                    }

                    if (looksEnumerated && !HasEnumLikeConstraint(property.Value))
                    {
                        enumCoverageMissingParams++;
                    }

                    if (looksStructured && !HasFormatLikeConstraint(property.Value))
                    {
                        formatHintMissingParams++;
                    }
                }

                if (isRequired && IsArrayType(property.Value) && MissingRequiredArrayShape(property.Value))
                {
                    requiredArrayParamsMissingShape++;
                }
            }

            if (paramCount > 0)
            {
                toolScore -= (undescribedParams / (double)paramCount) * 30.0;
                toolScore -= (vagueStringParams / (double)paramCount) * 20.0;
                toolScore -= (requiredArrayParamsMissingShape / (double)paramCount) * 15.0;
                toolScore -= (enumCoverageMissingParams / (double)paramCount) * 12.0;
                toolScore -= (formatHintMissingParams / (double)paramCount) * 12.0;
            }

            AddFindingIfAny(
                findings,
                undescribedParams > 0,
                ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions,
                ValidationFindingSeverity.Medium,
                tool.Name,
                $"Tool '{tool.Name}': {undescribedParams}/{paramCount} parameters lack descriptions (increases hallucination risk)",
                "Add clear descriptions to each parameter so agents can generate valid arguments without guessing.");

            AddFindingIfAny(
                findings,
                vagueStringParams > 0,
                ValidationFindingRuleIds.AiReadinessVagueStringSchema,
                ValidationFindingSeverity.Medium,
                tool.Name,
                $"Tool '{tool.Name}': {vagueStringParams}/{paramCount} string parameters have no enum/pattern/format constraint",
                "Constrain string parameters with enum, pattern, or format metadata when possible.");

            AddFindingIfAny(
                findings,
                requiredArrayParamsMissingShape > 0,
                ValidationFindingRuleIds.AiReadinessRequiredArraySchema,
                ValidationFindingSeverity.Medium,
                tool.Name,
                $"Tool '{tool.Name}': {requiredArrayParamsMissingShape}/{paramCount} required array parameters lack item schemas or minItems guidance",
                "Required array parameters should declare items schemas and minItems when empty arrays are not meaningful.");

            AddFindingIfAny(
                findings,
                enumCoverageMissingParams > 0,
                ValidationFindingRuleIds.AiReadinessEnumCoverageMissing,
                ValidationFindingSeverity.Low,
                tool.Name,
                $"Tool '{tool.Name}': {enumCoverageMissingParams}/{paramCount} string parameters look like fixed-choice fields but do not declare enum/const choices",
                "Use enum, const, oneOf, or anyOf so agents can choose from explicit valid values instead of guessing.");

            AddFindingIfAny(
                findings,
                formatHintMissingParams > 0,
                ValidationFindingRuleIds.AiReadinessFormatHintMissing,
                ValidationFindingSeverity.Low,
                tool.Name,
                $"Tool '{tool.Name}': {formatHintMissingParams}/{paramCount} string parameters look like structured values but do not declare format/pattern hints",
                "Add format or pattern metadata for URLs, URIs, dates, email addresses, UUIDs, and similar structured strings.");

            totalScore += Math.Max(0, toolScore);
            scored++;
        }

        var score = scored > 0 ? Math.Round(totalScore / scored, 1) : -1;
        var payloadChars = totalPayloadChars ?? rawJson?.Length ?? 0;
        long estimatedTokenCount = 0;

        if (payloadChars > 0)
        {
            estimatedTokenCount = payloadChars / 4;
            if (estimatedTokenCount > 32000)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.AiReadinessTokenBudgetExceeded,
                    Category = "AiReadiness",
                    Component = "tools/list",
                    Severity = ValidationFindingSeverity.High,
                    Summary = $"⚠️ tools/list response is ~{estimatedTokenCount:N0} tokens — exceeds typical 32k context window. AI agents may truncate tool metadata.",
                    Recommendation = "Reduce tool discovery payload size or split metadata so large servers remain usable by agents.",
                    Metadata =
                    {
                        ["tokenCount"] = estimatedTokenCount.ToString(),
                        [AiReadinessEvidenceKinds.MetadataKey] = AiReadinessEvidenceKinds.DeterministicPayloadHeuristic,
                        [AiReadinessEvidenceKinds.ModelEvaluationImpactKey] = AiReadinessEvidenceKinds.NotMeasuredModelImpact
                    }
                });
                score = Math.Max(0, score - 10);
            }
            else if (estimatedTokenCount > 8000)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.AiReadinessTokenBudgetWarning,
                    Category = "AiReadiness",
                    Component = "tools/list",
                    Severity = ValidationFindingSeverity.Low,
                    Summary = $"ℹ️ tools/list response is ~{estimatedTokenCount:N0} tokens — consider reducing descriptions for token efficiency.",
                    Recommendation = "Trim overly long descriptions and examples to improve agent discovery efficiency.",
                    Metadata =
                    {
                        ["tokenCount"] = estimatedTokenCount.ToString(),
                        [AiReadinessEvidenceKinds.MetadataKey] = AiReadinessEvidenceKinds.DeterministicPayloadHeuristic,
                        [AiReadinessEvidenceKinds.ModelEvaluationImpactKey] = AiReadinessEvidenceKinds.NotMeasuredModelImpact
                    }
                });
            }
        }

        string? summaryIssue = null;
        if (score >= 0)
        {
            var grade = score >= 80 ? "Good" : score >= 50 ? "Fair" : "Poor";
            summaryIssue = $"🤖 AI Readiness Score: {score:F0}/100 ({grade})";
        }

        return new ToolAiReadinessAnalysis
        {
            Score = score,
            EstimatedTokenCount = estimatedTokenCount,
            SummaryIssue = summaryIssue,
            Findings = findings
        };
    }

    public ToolErrorAiReadinessAssessment AnalyzeErrorResponse(string toolName, string rawJson, int errorCode, string errorMessage)
    {
        int score = 0;
        var insights = new List<string>();
        var textLower = (errorMessage ?? string.Empty).ToLowerInvariant();

        if (errorCode is -32602 or -32600 or -32601 or -32603 or -32700)
        {
            score += 20;
        }
        else
        {
            insights.Add("Non-standard error code — LLM may not recognize the failure type");
        }

        bool mentionsParam = false;
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var fullText = error.ToString().ToLowerInvariant();
                if (fullText.Contains("param") || fullText.Contains("argument") || fullText.Contains("field") ||
                    fullText.Contains("required") || fullText.Contains("missing"))
                {
                    mentionsParam = true;
                    score += 25;
                }

                if (fullText.Contains("expected") || fullText.Contains("must be") || fullText.Contains("should be") ||
                    fullText.Contains("type") || fullText.Contains("format") || fullText.Contains("valid"))
                {
                    score += 20;
                }
                else
                {
                    insights.Add("Error doesn't describe expected format — LLM can't self-correct");
                }

                if (error.TryGetProperty("data", out _))
                {
                    score += 15;
                }

                if ((errorMessage?.Length ?? 0) > 20)
                {
                    score += 10;
                }
                else
                {
                    insights.Add("Error message too short — LLM gets no useful context");
                }
            }
        }
        catch
        {
            insights.Add("Error payload was not parseable as JSON-RPC — LLM loses structured correction signals");
        }

        if (!mentionsParam)
        {
            insights.Add("Error doesn't mention which parameter is wrong — LLM will guess blindly");
        }

        if (rawJson.Contains("\"isError\"", StringComparison.Ordinal))
        {
            score += 10;
        }

        score = Math.Min(100, score);

        var grade = score >= 70 ? "Pro-LLM" : score >= 40 ? "Neutral" : "Anti-LLM";
        var gradeIcon = score >= 70 ? "🟢" : score >= 40 ? "🟡" : "🔴";
        var summary = $"{gradeIcon} LLM-Friendliness: {score}/100 ({grade}) — {(score >= 70 ? "Error helps AI self-correct" : score >= 40 ? "Error partially helpful for AI" : "Error will cause AI hallucination/loops")}";

        return new ToolErrorAiReadinessAssessment
        {
            Finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolLlmFriendliness,
                Category = "AiReadiness",
                Component = toolName,
                Severity = score >= 70 ? ValidationFindingSeverity.Info : score >= 40 ? ValidationFindingSeverity.Medium : ValidationFindingSeverity.High,
                Summary = summary,
                Recommendation = "Return specific, structured errors that identify the invalid argument and expected shape.",
                Metadata =
                {
                    ["score"] = score.ToString(),
                    ["grade"] = grade,
                    ["messagePreview"] = Truncate(textLower, 120),
                    [AiReadinessEvidenceKinds.MetadataKey] = AiReadinessEvidenceKinds.DeterministicErrorHeuristic,
                    [AiReadinessEvidenceKinds.ModelEvaluationImpactKey] = AiReadinessEvidenceKinds.NotMeasuredModelImpact
                }
            },
            SupportingIssues = score < 70 ? insights.Take(2).ToList() : Array.Empty<string>()
        };
    }

    private static void AddFindingIfAny(List<ValidationFinding> findings, bool condition, string ruleId, ValidationFindingSeverity severity, string component, string summary, string recommendation)
    {
        if (!condition)
        {
            return;
        }

        findings.Add(new ValidationFinding
        {
            RuleId = ruleId,
            Category = "AiReadiness",
            Component = component,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            Metadata =
            {
                [AiReadinessEvidenceKinds.MetadataKey] = AiReadinessEvidenceKinds.DeterministicSchemaHeuristic,
                [AiReadinessEvidenceKinds.ModelEvaluationImpactKey] = AiReadinessEvidenceKinds.NotMeasuredModelImpact
            }
        });
    }

    private static HashSet<string> GetRequiredPropertyNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (schema.ValueKind != JsonValueKind.Undefined &&
                schema.TryGetProperty("required", out var required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in required.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        var name = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return names;
    }

    private static bool IsArrayType(JsonElement propertySchema)
    {
        return propertySchema.TryGetProperty("type", out var type) && type.GetString() == "array";
    }

    private static bool MissingRequiredArrayShape(JsonElement propertySchema)
    {
        var hasItems = propertySchema.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        var hasMinItems = propertySchema.TryGetProperty("minItems", out var minItems) &&
            minItems.ValueKind == JsonValueKind.Number &&
            minItems.TryGetInt32(out _);

        return !hasItems || !hasMinItems;
    }

    private static bool HasEnumLikeConstraint(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array && enumValues.GetArrayLength() > 0)
        {
            return true;
        }

        if (propertySchema.TryGetProperty("const", out var constValue) && constValue.ValueKind != JsonValueKind.Undefined && constValue.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        return HasConstrainedAlternatives(propertySchema, "oneOf") || HasConstrainedAlternatives(propertySchema, "anyOf");
    }

    private static bool HasFormatLikeConstraint(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(format.GetString()))
        {
            return true;
        }

        if (propertySchema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pattern.GetString()))
        {
            return true;
        }

        return false;
    }

    private static bool HasConstrainedAlternatives(JsonElement propertySchema, string propertyName)
    {
        if (!propertySchema.TryGetProperty(propertyName, out var alternatives) || alternatives.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var alternative in alternatives.EnumerateArray())
        {
            if (alternative.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if ((alternative.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array && enumValues.GetArrayLength() > 0) ||
                (alternative.TryGetProperty("const", out var constValue) && constValue.ValueKind != JsonValueKind.Null && constValue.ValueKind != JsonValueKind.Undefined))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSemanticGuidance(JsonElement propertySchema)
    {
        if (HasEnumLikeConstraint(propertySchema) || HasFormatLikeConstraint(propertySchema))
        {
            return true;
        }

        if (HasMeaningfulText(GetSchemaText(propertySchema, "description")) ||
            HasMeaningfulText(GetSchemaText(propertySchema, "title")))
        {
            return true;
        }

        if (propertySchema.TryGetProperty("examples", out var examples) &&
            examples.ValueKind == JsonValueKind.Array &&
            examples.GetArrayLength() > 0)
        {
            return true;
        }

        if (propertySchema.TryGetProperty("default", out var defaultValue) &&
            defaultValue.ValueKind != JsonValueKind.Null &&
            defaultValue.ValueKind != JsonValueKind.Undefined)
        {
            return true;
        }

        if ((propertySchema.TryGetProperty("minLength", out var minLength) && minLength.ValueKind == JsonValueKind.Number) ||
            (propertySchema.TryGetProperty("maxLength", out var maxLength) && maxLength.ValueKind == JsonValueKind.Number))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeEnumeratedChoice(string propertyName, JsonElement propertySchema)
    {
        var candidateText = $"{propertyName} {GetSchemaText(propertySchema, "description")} {GetSchemaText(propertySchema, "title")}";
        string[] markers = ["mode", "type", "kind", "status", "state", "level", "action", "scope", "sort", "order", "direction"];
        return markers.Any(marker => candidateText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeStructuredFormatValue(string propertyName, JsonElement propertySchema)
    {
        var candidateText = $"{propertyName} {GetSchemaText(propertySchema, "description")} {GetSchemaText(propertySchema, "title")}";
        string[] markers = ["url", "uri", "email", "date", "time", "timestamp", "uuid", "guid", "hostname", "domain"];
        return markers.Any(marker => candidateText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMeaningfulText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 8;
    }

    private static string GetSchemaText(JsonElement propertySchema, string propertyName)
    {
        return propertySchema.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string Truncate(string value, int length)
    {
        return value.Length <= length ? value : value[..length];
    }
}