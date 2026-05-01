using System.Text;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Registries;

namespace Mcp.Benchmark.Infrastructure.Rules.Protocol;

public sealed class ContentTypeRule : IVersionedValidationRule<ProtocolValidationContext>
{
    private readonly ProtocolRuleMatrixEntry _matrixEntry;

    public ContentTypeRule()
        : this(BuiltInProtocolRuleMatrix.GetRequired(BuiltInProtocolRuleMatrix.RuleIds.HttpRequestContentType))
    {
    }

    internal ContentTypeRule(ProtocolRuleMatrixEntry matrixEntry)
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
