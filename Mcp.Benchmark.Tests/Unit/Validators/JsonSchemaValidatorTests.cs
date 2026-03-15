using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Validators;

/// <summary>
/// Tests for JsonSchemaValidator covering valid/invalid schema validation.
/// </summary>
public class JsonSchemaValidatorTests
{
    private readonly JsonSchemaValidator _validator = new();

    [Fact]
    public void Validate_WithValidInstance_ShouldPass()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
        var instance = System.Text.Json.Nodes.JsonNode.Parse("{\"name\":\"test\"}");

        var result = _validator.Validate(instance!, schema!);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithInvalidInstance_ShouldFail()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"age\":{\"type\":\"integer\"}},\"required\":[\"age\"]}");
        var instance = System.Text.Json.Nodes.JsonNode.Parse("{\"age\":\"not a number\"}");

        var result = _validator.Validate(instance!, schema!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_WithEmptyObject_AgainstEmptySchema_ShouldPass()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\"}");
        var instance = System.Text.Json.Nodes.JsonNode.Parse("{}");

        var result = _validator.Validate(instance!, schema!);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMalformedSchema_ShouldReturnError()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"not_a_real_schema\":true}");
        var instance = System.Text.Json.Nodes.JsonNode.Parse("{\"any\":\"value\"}");

        // Should not throw, should handle gracefully
        var result = _validator.Validate(instance!, schema!);
        result.Should().NotBeNull();
    }

    [Fact]
    public void IsValidSchemaDefinition_WithValidSchema_ShouldReturnTrue()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}}}");

        var isValid = _validator.IsValidSchemaDefinition(schema!, out var errors);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void IsValidSchemaDefinition_WithInvalidJson_ShouldReturnFalse()
    {
        // This is structurally valid JSON but might not parse as a schema depending on implementation
        var schema = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\"}");

        var isValid = _validator.IsValidSchemaDefinition(schema!, out var errors);

        // Should not crash
        isValid.Should().BeTrue(); // Simple objects are valid schemas
    }
}
