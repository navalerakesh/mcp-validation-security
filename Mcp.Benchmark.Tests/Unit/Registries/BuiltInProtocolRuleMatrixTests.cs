using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;

namespace Mcp.Benchmark.Tests.Unit.Registries;

public class BuiltInProtocolRuleMatrixTests
{
    [Fact]
    public void Resolve_ForStreamableHttp20251125_ShouldIncludeLifecycleAndHttpRulesOnly()
    {
        var context = CreateContext("http");

        var ruleIds = BuiltInProtocolRuleMatrix.Resolve(context)
            .Select(entry => entry.RuleId)
            .ToList();

        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.InitializeFirst);
        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.HttpProtocolVersionHeader);
        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.HttpSessionIdPropagation);
        ruleIds.Should().NotContain(BuiltInProtocolRuleMatrix.RuleIds.StdioNewlineFraming);
    }

    [Fact]
    public void Resolve_ForStdio20251125_ShouldIncludeLifecycleAndStdioRulesOnly()
    {
        var context = CreateContext("stdio");

        var ruleIds = BuiltInProtocolRuleMatrix.Resolve(context)
            .Select(entry => entry.RuleId)
            .ToList();

        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.InitializeFirst);
        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.StdioNewlineFraming);
        ruleIds.Should().Contain(BuiltInProtocolRuleMatrix.RuleIds.StdioStdoutMcpOnly);
        ruleIds.Should().NotContain(BuiltInProtocolRuleMatrix.RuleIds.HttpProtocolVersionHeader);
    }

    [Fact]
    public void ProtocolRuleRegistry_ShouldUseMatrixApplicabilityForConcreteRules()
    {
        var registry = new ProtocolRuleRegistry();

        var httpRules = registry.Resolve(CreateContext("http"));
        var stdioRules = registry.Resolve(CreateContext("stdio"));

        httpRules.OfType<ContentTypeRule>().Should().ContainSingle();
        httpRules.OfType<CaseSensitivityRule>().Should().ContainSingle();
        stdioRules.OfType<ContentTypeRule>().Should().BeEmpty();
        stdioRules.OfType<CaseSensitivityRule>().Should().ContainSingle();
    }

    [Fact]
    public void ConcreteRules_ShouldExposeMatrixDescriptors()
    {
        var contentType = new ContentTypeRule();
        var caseSensitivity = new CaseSensitivityRule();

        contentType.Id.Should().Be(BuiltInProtocolRuleMatrix.RuleIds.HttpRequestContentType);
        contentType.Descriptor.SpecReference.Should().Be(BuiltInProtocolRuleMatrix.GetRequired(BuiltInProtocolRuleMatrix.RuleIds.HttpRequestContentType).SpecReference);
        caseSensitivity.Id.Should().Be(BuiltInProtocolRuleMatrix.RuleIds.JsonRpcCaseSensitive);
        caseSensitivity.Applicability.ProtocolVersions.Should().Contain("2025-11-25");
    }

    private static ValidationApplicabilityContext CreateContext(string transport)
    {
        return new ValidationApplicabilityContext
        {
            NegotiatedProtocolVersion = "2025-11-25",
            SchemaVersion = "2025-11-25",
            Transport = transport,
            AccessMode = "public"
        };
    }
}
