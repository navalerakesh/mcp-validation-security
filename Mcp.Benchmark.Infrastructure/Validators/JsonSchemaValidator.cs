using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// Implementation of ISchemaValidator using JsonSchema.Net.
/// Supports JSON Schema Draft 2020-12.
/// </summary>
public class JsonSchemaValidator : ISchemaValidator
{
    private static readonly Dialect ExtensibleDraft202012 = CreateExtensibleDraft202012();

    public SchemaValidationResult Validate(JsonNode instance, JsonNode schema)
    {
        try
        {
            var schemaForEvaluation = schema.DeepClone();
            if (schemaForEvaluation is JsonObject schemaObject)
            {
                schemaObject.Remove("$id");
            }
            var jsonSchema = ParseSchema(schemaForEvaluation);
            
            // Convert JsonNode to JsonElement for evaluation
            // Note: JsonSchema.Net supports JsonNode, but we are seeing compilation errors suggesting it wants JsonElement.
            // This conversion ensures compatibility.
            using var doc = JsonDocument.Parse(instance.ToJsonString());

            // Evaluate the instance against the schema
            var result = jsonSchema.Evaluate(doc.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                AddAnnotationForUnknownKeywords = true
            });

            if (result.IsValid)
            {
                return new SchemaValidationResult { IsValid = true };
            }

            // Collect errors
            var errors = new List<string>();
            
            // Flatten the error list
            if (result.Details != null) 
            {
                foreach (var detail in result.Details)
                {
                    if (!detail.IsValid && detail.Errors != null)
                    {
                        foreach (var error in detail.Errors)
                        {
                            errors.Add($"Location: {detail.InstanceLocation}, Error: {error.Value}");
                        }
                    }
                }
            }
            else if (!result.IsValid)
            {
                 if (result.Errors != null)
                 {
                     foreach (var error in result.Errors)
                     {
                         errors.Add($"Error: {error.Value}");
                     }
                 }
                 else
                 {
                     errors.Add("Validation failed (no detailed errors returned).");
                 }
            }

            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = new List<string> { $"Schema processing error: {ex.Message}" }
            };
        }
    }

    public bool IsValidSchemaDefinition(JsonNode schema, out List<string> errors)
    {
        errors = new List<string>();
        try
        {
            // Parse the schema to ensure it's valid JSON Schema structure
            var schemaForEvaluation = schema.DeepClone();
            if (schemaForEvaluation is JsonObject schemaObject)
            {
                schemaObject.Remove("$id");
            }
            _ = ParseSchema(schemaForEvaluation);
            
            // Ideally we would validate against the meta-schema here, 
            // but successful parsing is sufficient for basic structural validity.
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Invalid JSON Schema definition: {ex.Message}");
            return false;
        }
    }

    private static JsonSchema ParseSchema(JsonNode schema) =>
        JsonSchema.FromText(
            schema.ToJsonString(),
            new BuildOptions { Dialect = ExtensibleDraft202012 });

    private static Dialect CreateExtensibleDraft202012()
    {
        var vocabularies = new[]
        {
            Vocabulary.Draft202012_Core,
            Vocabulary.Draft202012_Applicator,
            Vocabulary.Draft202012_Unevaluated,
            Vocabulary.Draft202012_Validation,
            Vocabulary.Draft202012_MetaData,
            Vocabulary.Draft202012_FormatAnnotation,
            Vocabulary.Draft202012_Content
        };
        return new Dialect(vocabularies.SelectMany(vocabulary => vocabulary.Keywords))
        {
            Id = Dialect.Draft202012.Id,
            RefIgnoresSiblingKeywords = Dialect.Draft202012.RefIgnoresSiblingKeywords,
            AllowUnknownKeywords = true
        };
    }
}
