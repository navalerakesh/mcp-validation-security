using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Pointer;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services;

internal sealed class SchemaInspectionContext
{
    private const int MaximumIndexSteps = 4096;
    private const int MaximumSchemaDepth = 64;
    private const int MaximumInspectionSteps = 4096;
    private const int MaximumReferenceCharacters = 4096;
    private const int MaximumAlternatives = 32;
    private const int MaximumGenerationSchemaCharacters = 64 * 1024;
    private const int MaximumGeneratedCharacters = 64 * 1024;
    private const int MaximumGeneratedValueCharacters = 4096;
    private const string SupportedDialect = "https://json-schema.org/draft/2020-12/schema";
    private readonly JsonElement _root;
    private readonly Dictionary<JsonElement, SchemaInspectionStatus> _schemas = new();
    private int _remainingIndexSteps = MaximumIndexSteps;
    private int _remainingSteps = MaximumInspectionSteps;
    private bool _indexExhausted;
    private int _remainingGeneratedValues = 64;
    private int _remainingGeneratedCharacters = MaximumGeneratedCharacters;

    internal SchemaInspectionContext(JsonElement root)
    {
        _root = root;
        Index(root, 0, isRoot: true);
    }

    internal SchemaInspectionStatus InspectConjunction(JsonElement schema, out IReadOnlyList<JsonElement> clauses)
    {
        var collected = new List<JsonElement>();
        var status = _indexExhausted ? SchemaInspectionStatus.BudgetExceeded : Visit(schema, new HashSet<JsonElement>(), collected, 0);
        clauses = collected.Distinct().ToArray();
        if (status == SchemaInspectionStatus.Complete && GetAllowedTypes(clauses) is { Count: 0 })
            return SchemaInspectionStatus.Unsupported;
        return status;
    }

    internal static bool HasType(IReadOnlyCollection<JsonElement> clauses, string type) => GetAllowedTypes(clauses)?.Contains(type) == true;

    internal static bool HasCompatibleTypes(IEnumerable<JsonElement> clauses) => GetAllowedTypes(clauses) is not { Count: 0 };

    internal SchemaInspectionStatus InspectAlternatives(IEnumerable<JsonElement> definitions, out IReadOnlyList<IReadOnlyList<JsonElement>> alternatives)
    {
        alternatives = [];
        if (_indexExhausted) return SchemaInspectionStatus.BudgetExceeded;
        var combined = new List<List<JsonElement>> { new() };
        foreach (var definition in definitions)
        {
            var status = Expand(definition, new HashSet<JsonElement>(), 0, out var expanded);
            if (status != SchemaInspectionStatus.Complete) return status;
            status = CombineAlternatives(combined, expanded, out combined);
            if (status != SchemaInspectionStatus.Complete) return status;
        }
        alternatives = combined.Where(HasCompatibleTypes).Select(branch => (IReadOnlyList<JsonElement>)branch).ToArray();
        return alternatives.Count == 0 ? SchemaInspectionStatus.Unsupported : SchemaInspectionStatus.Complete;
    }

    private SchemaInspectionStatus Expand(JsonElement schema, HashSet<JsonElement> path, int depth, out List<List<JsonElement>> alternatives)
    {
        alternatives = [];
        if (_remainingSteps-- <= 0 || depth > MaximumSchemaDepth) return SchemaInspectionStatus.BudgetExceeded;
        if (!_schemas.TryGetValue(schema, out var status)) return SchemaInspectionStatus.Unsupported;
        if (status != SchemaInspectionStatus.Complete) return status;
        if (!path.Add(schema)) return SchemaInspectionStatus.CyclicReference;
        try
        {
            if (schema.ValueKind == JsonValueKind.False) return SchemaInspectionStatus.Complete;
            if (schema.ValueKind == JsonValueKind.True)
            {
                alternatives.Add([]);
                return SchemaInspectionStatus.Complete;
            }
            alternatives.Add([schema]);
            if (schema.TryGetProperty("$ref", out var reference))
            {
                if (reference.ValueKind != JsonValueKind.String) return SchemaInspectionStatus.UnresolvedReference;
                var value = reference.GetString()!;
                if (value.Length > MaximumReferenceCharacters) return SchemaInspectionStatus.BudgetExceeded;
                if (!value.StartsWith('#')) return SchemaInspectionStatus.Unsupported;
                if (!JsonPointer.TryParse(value, out var pointer)) return SchemaInspectionStatus.UnresolvedReference;
                var target = pointer.Evaluate(_root);
                if (target is null) return SchemaInspectionStatus.UnresolvedReference;
                status = Expand(target.Value, path, depth + 1, out var referenced);
                if (status != SchemaInspectionStatus.Complete) return status;
                status = CombineAlternatives(alternatives, referenced, out alternatives);
                if (status != SchemaInspectionStatus.Complete) return status;
            }
            foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
            {
                if (!schema.TryGetProperty(keyword, out var children)) continue;
                if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0) return SchemaInspectionStatus.Unsupported;
                var union = new List<List<JsonElement>>();
                foreach (var child in children.EnumerateArray())
                {
                    status = Expand(child, path, depth + 1, out var branches);
                    if (status != SchemaInspectionStatus.Complete) return status;
                    if (keyword == "allOf")
                    {
                        status = CombineAlternatives(alternatives, branches, out alternatives);
                        if (status != SchemaInspectionStatus.Complete) return status;
                    }
                    else
                    {
                        if (union.Count + branches.Count > MaximumAlternatives) return SchemaInspectionStatus.BudgetExceeded;
                        union.AddRange(branches);
                    }
                }
                if (keyword != "allOf")
                {
                    status = CombineAlternatives(alternatives, union, out alternatives);
                    if (status != SchemaInspectionStatus.Complete) return status;
                }
            }
            return SchemaInspectionStatus.Complete;
        }
        finally
        {
            path.Remove(schema);
        }
    }

    private SchemaInspectionStatus CombineAlternatives(List<List<JsonElement>> left, List<List<JsonElement>> right,
        out List<List<JsonElement>> combined)
    {
        combined = [];
        if ((long)left.Count * right.Count > MaximumAlternatives) return SchemaInspectionStatus.BudgetExceeded;
        foreach (var first in left)
            foreach (var second in right)
            {
                _remainingSteps -= first.Count + second.Count + 1;
                if (_remainingSteps < 0) return SchemaInspectionStatus.BudgetExceeded;
                combined.Add(first.Concat(second).Distinct().ToList());
            }
        return SchemaInspectionStatus.Complete;
    }

    internal static (Dictionary<string, List<JsonElement>> Properties, HashSet<string> Required) GetObjectShape(IEnumerable<JsonElement> clauses)
    {
        var properties = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var additional = new List<JsonElement>();
        foreach (var clause in clauses)
        {
            if (clause.TryGetProperty("additionalProperties", out var additionalSchema)) additional.Add(additionalSchema);
            if (clause.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array)
                foreach (var name in names.EnumerateArray())
                    if (name.ValueKind == JsonValueKind.String) required.Add(name.GetString()!);
            if (!clause.TryGetProperty("properties", out var declared) || declared.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in declared.EnumerateObject())
            {
                if (!properties.TryGetValue(property.Name, out var schemas)) properties[property.Name] = schemas = [];
                schemas.Add(property.Value);
            }
        }
        foreach (var name in required.Where(name => !properties.ContainsKey(name)))
            properties[name] = additional.ToList();
        return (properties, required);
    }

    internal bool TryCreateObjectCandidate(out JsonObject? candidate)
    {
        candidate = null;
        if (_indexExhausted || _root.ValueKind == JsonValueKind.Undefined ||
            _root.GetRawText().Length > MaximumGenerationSchemaCharacters ||
            _schemas.Values.Any(status => status != SchemaInspectionStatus.Complete)) return false;
        foreach (var schema in _schemas.Keys)
        {
            if (schema.ValueKind == JsonValueKind.Object &&
                (schema.TryGetProperty("pattern", out _) || schema.TryGetProperty("patternProperties", out _) ||
                 schema.TryGetProperty("anyOf", out _) || schema.TryGetProperty("oneOf", out _))) return false;
            if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("$ref", out var reference)) continue;
            if (reference.ValueKind != JsonValueKind.String) return false;
            var value = reference.GetString()!;
            if (value.Length > MaximumReferenceCharacters || !value.StartsWith('#') || !JsonPointer.TryParse(value, out var pointer)) return false;
            var target = pointer.Evaluate(_root);
            if (target is null || !_schemas.ContainsKey(target.Value)) return false;
            if (InspectConjunction(schema, out _) != SchemaInspectionStatus.Complete) return false;
        }
        if (!TryGenerate([_root], 0, rootObject: true, out var generated) || generated is not JsonObject objectCandidate) return false;
        if (objectCandidate.ToJsonString().Length > MaximumGeneratedCharacters) return false;
        candidate = objectCandidate;
        return true;
    }

    private bool TryGenerate(IEnumerable<JsonElement> definitions, int depth, bool rootObject, out JsonNode? value)
    {
        value = null;
        if (_remainingGeneratedValues-- <= 0 || depth > 16) return false;
        var clauses = new List<JsonElement>();
        foreach (var definition in definitions)
        {
            if (InspectConjunction(definition, out var inspected) != SchemaInspectionStatus.Complete) return false;
            clauses.AddRange(inspected);
        }
        if (!HasCompatibleTypes(clauses)) return false;
        foreach (var clause in clauses)
        {
            if (clause.TryGetProperty("const", out var constant))
            {
                return TryCloneCandidateValue(constant, out value);
            }
            if (clause.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                return TryCloneCandidateValue(choices[0], out value);
            }
        }
        var types = GetAllowedTypes(clauses);
        if (rootObject || types?.Contains("object") == true || clauses.Any(clause => clause.TryGetProperty("properties", out _)))
        {
            var (properties, required) = GetObjectShape(clauses);
            if (required.Any(name => !properties.ContainsKey(name))) return false;
            var result = new JsonObject();
            foreach (var property in properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                if (property.Key.Length > MaximumGeneratedValueCharacters) return false;
                _remainingGeneratedCharacters -= property.Key.Length + 4;
                if (_remainingGeneratedCharacters < 0) return false;
                if (TryGenerate(property.Value, depth + 1, rootObject: false, out var child)) result[property.Key] = child;
                else if (required.Contains(property.Key)) return false;
            }
            value = result;
            return true;
        }
        if (types?.Contains("array") == true) value = new JsonArray();
        else if (types?.Contains("integer") == true || types?.Contains("number") == true) value = JsonValue.Create(1);
        else if (types?.Contains("boolean") == true) value = JsonValue.Create(true);
        else if (types?.SetEquals(["null"]) == true) value = null;
        else value = JsonValue.Create("test-check");
        return true;
    }

    private bool TryCloneCandidateValue(JsonElement source, out JsonNode? value)
    {
        value = null;
        var json = source.GetRawText();
        if (json.Length > MaximumGeneratedValueCharacters) return false;
        _remainingGeneratedCharacters -= json.Length;
        if (_remainingGeneratedCharacters < 0) return false;
        value = JsonNode.Parse(json);
        return true;
    }

    private static HashSet<string>? GetAllowedTypes(IEnumerable<JsonElement> clauses)
    {
        HashSet<string>? allowed = null;
        foreach (var clause in clauses)
        {
            if (clause.ValueKind != JsonValueKind.Object || !clause.TryGetProperty("type", out var type)) continue;
            var current = new HashSet<string>(StringComparer.Ordinal);
            if (type.ValueKind == JsonValueKind.String) current.Add(type.GetString()!);
            else if (type.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in type.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) return [];
                    current.Add(entry.GetString()!);
                }
            }
            else return [];
            if (current.Any(value => value is not ("string" or "array" or "object" or "number" or "integer" or "boolean" or "null"))) return [];
            if (current.Contains("number")) current.Add("integer");
            if (allowed is null) allowed = current;
            else allowed.IntersectWith(current);
        }
        return allowed;
    }

    private SchemaInspectionStatus Visit(JsonElement schema, HashSet<JsonElement> path, List<JsonElement> clauses, int depth)
    {
        if (_remainingSteps-- <= 0 || depth > MaximumSchemaDepth) return SchemaInspectionStatus.BudgetExceeded;
        if (!_schemas.TryGetValue(schema, out var status)) return SchemaInspectionStatus.Unsupported;
        if (status != SchemaInspectionStatus.Complete) return status;
        if (!path.Add(schema)) return SchemaInspectionStatus.CyclicReference;
        try
        {
            if (schema.ValueKind == JsonValueKind.False) return SchemaInspectionStatus.Unsupported;
            if (schema.ValueKind == JsonValueKind.True) return SchemaInspectionStatus.Complete;
            if (schema.TryGetProperty("anyOf", out _) || schema.TryGetProperty("oneOf", out _)) return SchemaInspectionStatus.Unsupported;
            clauses.Add(schema);
            if (schema.TryGetProperty("$ref", out var reference))
            {
                if (reference.ValueKind != JsonValueKind.String) return SchemaInspectionStatus.UnresolvedReference;
                var value = reference.GetString()!;
                if (value.Length > MaximumReferenceCharacters) return SchemaInspectionStatus.BudgetExceeded;
                if (!value.StartsWith('#')) return SchemaInspectionStatus.Unsupported;
                if (!JsonPointer.TryParse(value, out var pointer)) return SchemaInspectionStatus.UnresolvedReference;
                var target = pointer.Evaluate(_root);
                if (target is null) return SchemaInspectionStatus.UnresolvedReference;
                status = Visit(target.Value, path, clauses, depth + 1);
                if (status != SchemaInspectionStatus.Complete) return status;
            }
            if (schema.TryGetProperty("allOf", out var conjunction))
            {
                if (conjunction.ValueKind != JsonValueKind.Array || conjunction.GetArrayLength() == 0)
                    return SchemaInspectionStatus.Unsupported;
                foreach (var child in conjunction.EnumerateArray())
                {
                    status = Visit(child, path, clauses, depth + 1);
                    if (status != SchemaInspectionStatus.Complete) return status;
                }
            }
            return SchemaInspectionStatus.Complete;
        }
        finally
        {
            path.Remove(schema);
        }
    }

    private void Index(JsonElement schema, int depth, bool isRoot = false)
    {
        if (_remainingIndexSteps-- <= 0 || depth > MaximumSchemaDepth)
        {
            _indexExhausted = true;
            return;
        }
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _schemas[schema] = SchemaInspectionStatus.Complete;
            return;
        }
        if (schema.ValueKind != JsonValueKind.Object) return;
        _schemas[schema] = SchemaInspectionStatus.Complete;
        if (schema.TryGetProperty("$id", out var identifier) && (!isRoot || identifier.ValueKind != JsonValueKind.String) ||
            schema.TryGetProperty("$dynamicRef", out _) || schema.TryGetProperty("$recursiveRef", out _) ||
            schema.TryGetProperty("patternProperties", out _) || schema.TryGetProperty("dependentSchemas", out _) || schema.TryGetProperty("dependentRequired", out _) ||
            schema.TryGetProperty("if", out _) || schema.TryGetProperty("not", out _))
        {
            _schemas[schema] = SchemaInspectionStatus.Unsupported;
            return;
        }
        if (schema.TryGetProperty("$schema", out var dialect) &&
            (dialect.ValueKind != JsonValueKind.String || !dialect.ValueEquals(SupportedDialect) && !dialect.ValueEquals(SupportedDialect + "#")))
        {
            _schemas[schema] = SchemaInspectionStatus.Unsupported;
            return;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in schema.EnumerateObject())
        {
            if (_remainingIndexSteps-- <= 0)
            {
                _indexExhausted = true;
                return;
            }
            if (!names.Add(keyword.Name))
            {
                _schemas[schema] = SchemaInspectionStatus.Unsupported;
                return;
            }
            if (keyword.Name is "$defs" or "definitions" or "properties" or "patternProperties" or "dependentSchemas")
            {
                if (keyword.Value.ValueKind != JsonValueKind.Object)
                {
                    _schemas[schema] = SchemaInspectionStatus.Unsupported;
                    continue;
                }
                var childNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in keyword.Value.EnumerateObject())
                {
                    if (!childNames.Add(child.Name)) _schemas[schema] = SchemaInspectionStatus.Unsupported;
                    Index(child.Value, depth + 1);
                    if (_indexExhausted) return;
                }
            }
            else if (keyword.Name is "allOf" or "anyOf" or "oneOf" or "prefixItems")
            {
                if (keyword.Value.ValueKind != JsonValueKind.Array)
                {
                    _schemas[schema] = SchemaInspectionStatus.Unsupported;
                    continue;
                }
                foreach (var child in keyword.Value.EnumerateArray())
                {
                    Index(child, depth + 1);
                    if (_indexExhausted) return;
                }
            }
            else if (keyword.Name is "items" or "additionalProperties" or "contains" or "propertyNames" or "not" or
                "if" or "then" or "else" or "unevaluatedItems" or "unevaluatedProperties" or "contentSchema")
                Index(keyword.Value, depth + 1);
            else
                CountAnnotationData(keyword.Value, depth + 1);
            if (_indexExhausted) return;
        }
    }

    private void CountAnnotationData(JsonElement value, int depth)
    {
        if (_remainingIndexSteps-- <= 0 || depth > MaximumSchemaDepth)
        {
            _indexExhausted = true;
            return;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in value.EnumerateObject())
            {
                CountAnnotationData(child.Value, depth + 1);
                if (_indexExhausted) return;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
            {
                CountAnnotationData(child, depth + 1);
                if (_indexExhausted) return;
            }
        }
    }
}