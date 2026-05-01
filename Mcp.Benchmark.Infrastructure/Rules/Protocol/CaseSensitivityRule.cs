using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Registries;

namespace Mcp.Benchmark.Infrastructure.Rules.Protocol;

public sealed class CaseSensitivityRule : IVersionedValidationRule<ProtocolValidationContext>
{
    private readonly ProtocolRuleMatrixEntry _matrixEntry;

    public CaseSensitivityRule()
        : this(BuiltInProtocolRuleMatrix.GetRequired(BuiltInProtocolRuleMatrix.RuleIds.JsonRpcCaseSensitive))
    {
    }

    internal CaseSensitivityRule(ProtocolRuleMatrixEntry matrixEntry)
    {
        _matrixEntry = matrixEntry ?? throw new ArgumentNullException(nameof(matrixEntry));
    }

    public string Id => _matrixEntry.RuleId;
    public string Description => _matrixEntry.Title;
    public string SpecVersion => _matrixEntry.Applicability.ProtocolVersions.FirstOrDefault() ?? SchemaRegistryProtocolVersions.GetLatestVersion().Value;

    public ValidationRuleDescriptor Descriptor => new()
    {
        RuleId = Id,
        Source = _matrixEntry.Source,
        SpecReference = _matrixEntry.SpecReference
    };

    public ValidationApplicability Applicability => _matrixEntry.Applicability;

    public async Task<RuleResult> EvaluateAsync(ProtocolValidationContext context, CancellationToken cancellationToken)
    {
        // "JsonRpc" instead of "jsonrpc"
        var invalidCase = "{\"JsonRpc\": \"2.0\", \"method\": \"ping\", \"id\": 1}";
        var response = await context.Client.SendRawJsonAsync(context.Endpoint, invalidCase, cancellationToken);
        
        // Should fail or be treated as invalid request
        // 406 Not Acceptable is a valid rejection
        if (response.StatusCode == 406) 
        {
            return new RuleResult { IsCompliant = true, ScoreImpact = 0 };
        }

        if (!response.IsSuccess) 
        {
            return new RuleResult { IsCompliant = true, ScoreImpact = 0 };
        }

        // If 200 OK, check if it's an error response (Invalid Request)
        try
        {
            using var doc = JsonDocument.Parse(response.RawJson!);
            if (doc.RootElement.TryGetProperty("error", out _))
            {
                return new RuleResult { IsCompliant = true, ScoreImpact = 0 };
            }
        }
        catch
        {
            // JSON parsing failed, which is technically a pass for "not processing it as valid"
        }

        return new RuleResult 
        { 
            IsCompliant = false, 
            FailureReason = "Server accepted case-insensitive JSON-RPC members",
            Severity = ViolationSeverity.Low,
            ScoreImpact = 5.0
        };
    }
}
