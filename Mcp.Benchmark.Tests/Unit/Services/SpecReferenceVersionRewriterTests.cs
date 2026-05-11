using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class SpecReferenceVersionRewriterTests
{
    private static readonly string[] EmbeddedVersions =
    {
        "2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25"
    };

    [Fact]
    public void Apply_WithNegotiatedOlderVersion_RewritesViolationAndFindingUrls()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        Description = "Bad request",
                        Recommendation = "See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors for details.",
                        SpecReference = "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"
                    }
                }
            },
            ToolValidation = new ToolTestResult
            {
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = "X",
                        Recommendation = "See https://modelcontextprotocol.io/specification/2025-11-25/server/tools",
                        SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/server/tools"
                    }
                }
            }
        };
        result.ProtocolVersion = "2025-06-18";

        SpecReferenceVersionRewriter.Apply(result, "2025-06-18", EmbeddedVersions);

        result.ProtocolCompliance.Violations[0].SpecReference.Should().Be("https://spec.modelcontextprotocol.io/specification/2025-06-18/basic/json-rpc");
        result.ProtocolCompliance.Violations[0].Recommendation.Should().Contain("/specification/2025-06-18/basic/json-rpc#errors");
        result.ToolValidation.Findings[0].SpecReference.Should().Be("https://modelcontextprotocol.io/specification/2025-06-18/server/tools");
    }

    [Fact]
    public void Apply_WithUnknownVersion_LeavesUrlsUnchanged()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        SpecReference = "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"
                    }
                }
            }
        };

        SpecReferenceVersionRewriter.Apply(result, "9999-12-31", EmbeddedVersions);

        result.ProtocolCompliance.Violations[0].SpecReference
            .Should().Be("https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc");
    }

    [Fact]
    public void Apply_WithMatchingVersion_IsNoOp()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        SpecReference = "https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"
                    }
                }
            }
        };

        SpecReferenceVersionRewriter.Apply(result, "2025-11-25", EmbeddedVersions);

        result.ProtocolCompliance.Violations[0].SpecReference
            .Should().Be("https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc");
    }
}
