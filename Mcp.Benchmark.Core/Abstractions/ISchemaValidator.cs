using System.Text.Json.Nodes;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Defines a contract for validating JSON payloads against JSON Schemas.
/// Supports JSON Schema Draft 2020-12 as required by MCP 2025-11-25.
/// </summary>
public interface ISchemaValidator
{
    /// <summary>
    /// Validates a JSON instance against a schema.
    /// </summary>
    /// <param name="instance">The JSON instance to validate.</param>
    /// <param name="schema">The JSON schema definition.</param>
    /// <returns>A result indicating success or failure with error details.</returns>
    SchemaValidationResult Validate(JsonNode instance, JsonNode schema);

    /// <summary>
    /// Validates that a JSON node represents a valid JSON Schema.
    /// </summary>
    /// <param name="schema">The schema definition to validate.</param>
    /// <param name="errors">Output list of validation errors.</param>
    /// <returns>True if valid, otherwise false.</returns>
    bool IsValidSchemaDefinition(JsonNode schema, out List<string> errors);
}

/// <summary>
/// Represents the result of a schema validation operation.
/// </summary>
public class SchemaValidationResult
{
    /// <summary>
    /// Gets or sets whether the validation was successful.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the list of validation errors if any.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}
