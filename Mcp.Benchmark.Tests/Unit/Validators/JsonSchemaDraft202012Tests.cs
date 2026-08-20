using System.Text.Json.Nodes;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Validators;

public sealed class JsonSchemaDraft202012Tests
{
    private readonly JsonSchemaValidator _validator = new();

    [Fact]
    public void Validate_Draft202012UnevaluatedProperties_ShouldRejectAdditionalProperty()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "name": { "type": "string" } },
              "unevaluatedProperties": false
            }
            """)!;
        var instance = JsonNode.Parse("{\"name\":\"tool\",\"unexpected\":true}")!;

        _validator.Validate(instance, schema).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Draft202012PrefixItems_ShouldValidateTuplePositions()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "array",
              "prefixItems": [
                { "type": "string" },
                { "type": "integer" }
              ],
              "items": false
            }
            """)!;

        _validator.Validate(JsonNode.Parse("[\"ok\",1]")!, schema).IsValid.Should().BeTrue();
        _validator.Validate(JsonNode.Parse("[\"ok\",\"bad\"]")!, schema).IsValid.Should().BeFalse();
    }
}
