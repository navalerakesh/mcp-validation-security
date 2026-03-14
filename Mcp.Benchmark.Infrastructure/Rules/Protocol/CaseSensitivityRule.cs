using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Rules.Protocol;

public class CaseSensitivityRule : IValidationRule<ProtocolValidationContext>
{
    public string Id => "PROTOCOL-008";
    public string Description => "Case Sensitivity";
    public string SpecVersion => "2024-11-25";

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
