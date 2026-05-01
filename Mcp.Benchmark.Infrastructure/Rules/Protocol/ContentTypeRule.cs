using System.Text;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Rules.Protocol;

public sealed class ContentTypeRule : IVersionedValidationRule<ProtocolValidationContext>
{
    public string Id => "PROTOCOL-007";
    public string Description => "Content-Type Requirements";
    public string SpecVersion => SchemaRegistryProtocolVersions.GetLatestVersion().Value;

    public ValidationRuleDescriptor Descriptor => new()
    {
        RuleId = Id,
        Source = ValidationRuleSource.Spec,
        SpecReference = "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http"
    };

    public ValidationApplicability Applicability => new()
    {
        Transports = new[] { "http", "https" }
    };

    public async Task<RuleResult> EvaluateAsync(ProtocolValidationContext context, CancellationToken cancellationToken)
    {
        // Send valid JSON but with wrong Content-Type
        var content = new StringContent("{\"jsonrpc\": \"2.0\", \"method\": \"ping\", \"id\": 1}", Encoding.UTF8, "text/plain");
        var response = await context.Client.SendAsync(context.Endpoint, content, cancellationToken);
        
        // Strict servers should reject this with 415 (or 406 for older implementations).
        if (response.StatusCode == System.Net.HttpStatusCode.NotAcceptable || 
            response.StatusCode == System.Net.HttpStatusCode.UnsupportedMediaType)
        {
            return new RuleResult { IsCompliant = true, ScoreImpact = 0 };
        }

        if ((int)response.StatusCode is 401 or 403)
        {
            return new RuleResult { IsCompliant = true, ScoreImpact = 0 };
        }

        return new RuleResult
        {
            IsCompliant = false,
            FailureReason = $"Server accepted or mishandled a JSON-RPC request with Content-Type text/plain (HTTP {(int)response.StatusCode}).",
            Severity = ViolationSeverity.High,
            ScoreImpact = 10
        };
    }
}
