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
    public SchemaValidationResult Validate(JsonNode instance, JsonNode schema)
    {
        try
        {
            // Parse the schema
            var jsonSchema = JsonSchema.FromText(schema.ToJsonString());
            
            // Convert JsonNode to JsonElement for evaluation
            // Note: JsonSchema.Net supports JsonNode, but we are seeing compilation errors suggesting it wants JsonElement.
            // This conversion ensures compatibility.
            using var doc = JsonDocument.Parse(instance.ToJsonString());

            // Evaluate the instance against the schema
            var result = jsonSchema.Evaluate(doc.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
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
            var jsonSchema = JsonSchema.FromText(schema.ToJsonString());
            
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
}
