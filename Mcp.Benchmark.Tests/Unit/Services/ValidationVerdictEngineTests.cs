using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationVerdictEngineTests
{
    [Fact]
    public void Calculate_WithProtocolViolation_ShouldLinkDecisionToViolationAndObservationEvidence()
    {
        var violation = new ComplianceViolation
        {
            CheckId = "MCP.PROTOCOL.MESSAGE_ID",
            Category = "Protocol",
            Description = "Response omitted the JSON-RPC id.",
            Severity = ViolationSeverity.High,
            SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc",
            Recommendation = "Return the same JSON-RPC id in the response."
        };
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation> { violation }
            }
        };
        result.Evidence.Observations.Add(new ValidationObservation
        {
            Id = "protocol-message-id",
            LayerId = "protocol-core",
            Component = "Protocol",
            ObservationKind = "protocol-violation",
            RedactedPayloadPreview = "Observed response without an id member.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkId"] = violation.CheckId
            }
        });

        var assessment = ValidationVerdictEngine.Calculate(result);
        var decision = assessment.BlockingDecisions.Single(decision => decision.RuleId == violation.CheckId);

        decision.RelatedEvidenceIds.Should().Contain(ValidationEvidenceIdBuilder.ForComplianceViolation(violation));
        decision.RelatedEvidenceIds.Should().Contain("protocol-message-id");
        decision.EvidenceReferences.Should().Contain(reference =>
            reference.EvidenceId == ValidationEvidenceIdBuilder.ForComplianceViolation(violation)
            && reference.SpecReference == violation.SpecReference
            && reference.Remediation == violation.Recommendation);
        decision.EvidenceReferences.Should().Contain(reference =>
            reference.EvidenceId == "protocol-message-id"
            && reference.RedactedPayloadPreview == "Observed response without an id member.");
    }

    [Fact]
    public void Calculate_WithStructuredFinding_ShouldLinkDecisionToFindingEvidence()
    {
        var finding = new ValidationFinding
        {
            RuleId = "TOOL.AI.DESTRUCTIVE_HINT",
            Category = "ToolGuidance",
            Component = "delete_repo",
            Severity = ValidationFindingSeverity.High,
            Source = ValidationRuleSource.Guideline,
            Summary = "Destructive tool is missing confirmation guidance.",
            Recommendation = "Declare destructive behavior and require explicit confirmation.",
            Metadata = new Dictionary<string, string>
            {
                ["annotation"] = "destructiveHint"
            }
        };
        var result = new ValidationResult
        {
            ToolValidation = new ToolTestResult
            {
                Findings = new List<ValidationFinding> { finding }
            }
        };
        result.Evidence.Observations.Add(new ValidationObservation
        {
            Id = "tool-delete-repo",
            LayerId = "tool-surface",
            Component = "delete_repo",
            ObservationKind = "tool-result",
            RedactedPayloadPreview = "Tool metadata omitted annotations.destructiveHint.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ruleId"] = finding.RuleId
            }
        });

        var assessment = ValidationVerdictEngine.Calculate(result);
        var decision = assessment.BlockingDecisions.Single(decision => decision.RuleId == finding.RuleId);

        decision.RelatedEvidenceIds.Should().Contain(ValidationEvidenceIdBuilder.ForFinding(finding));
        decision.RelatedEvidenceIds.Should().Contain("tool-delete-repo");
        decision.EvidenceReferences.Should().Contain(reference =>
            reference.EvidenceId == ValidationEvidenceIdBuilder.ForFinding(finding)
            && reference.Remediation == finding.Recommendation);
    }

    [Fact]
    public void Calculate_WithCoverageDebt_ShouldLinkDecisionToCoverageAndProbeEvidence()
    {
        var coverage = new ValidationCoverageDeclaration
        {
            LayerId = "tool-surface",
            Scope = "tools-list",
            Status = ValidationCoverageStatus.AuthRequired,
            Blocker = ValidationEvidenceBlocker.AuthRequired,
            Confidence = EvidenceConfidenceLevel.Medium,
            Reason = "tools/list returned an authentication challenge.",
            ProbeContext = new ProbeContext
            {
                ProbeId = "probe-tools-list",
                RequestId = "rpc-1",
                Method = "tools/list",
                Transport = "http",
                AuthStatus = ProbeAuthStatus.AuthRequired,
                ResponseClassification = ProbeResponseClassification.AuthenticationChallenge,
                Confidence = EvidenceConfidenceLevel.Medium,
                StatusCode = 401,
                Reason = "WWW-Authenticate challenge observed."
            }
        };
        var result = new ValidationResult();
        result.Evidence.Coverage.Add(coverage);

        var assessment = ValidationVerdictEngine.Calculate(result);
        var decision = assessment.CoverageDecisions.Single();

        decision.RelatedEvidenceIds.Should().Contain(ValidationEvidenceIdBuilder.ForCoverage(coverage));
        decision.RelatedEvidenceIds.Should().Contain(ValidationEvidenceIdBuilder.ForProbe(coverage.ProbeContext));
        decision.EvidenceReferences.Should().Contain(reference =>
            reference.EvidenceKind == "probe-context"
            && reference.Metadata["statusCode"] == "401"
            && reference.RedactedPayloadPreview == "WWW-Authenticate challenge observed.");
    }

    [Fact]
    public void Calculate_WithCoveredLowConfidenceEvidence_ShouldRequireCoverageReview()
    {
        var result = new ValidationResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "tool-surface",
            Scope = "tools/list",
            Status = ValidationCoverageStatus.Covered,
            Confidence = EvidenceConfidenceLevel.Low,
            Reason = "Only partial parser-boundary evidence was available."
        });

        var assessment = ValidationVerdictEngine.Calculate(result);
        var decision = assessment.CoverageDecisions.Single();

        assessment.CoverageVerdict.Should().Be(ValidationVerdict.ReviewRequired);
        decision.DecisionId.Should().Be("coverage-confidence:tool-surface:tools-list:Low");
        decision.Summary.Should().Contain("Coverage confidence is Low for tool-surface/tools/list");
        ValidationVerdictEngine.IsPassing(assessment).Should().BeFalse();
    }

    [Fact]
    public void Calculate_WithMixedAuthoritySignals_ShouldOrderByNormativeAuthority()
    {
        var result = new ValidationResult
        {
            CriticalErrors = new List<string> { "Validator runtime error." },
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.SPEC.BLOCKER",
                        Category = "Protocol",
                        Description = "Spec violation.",
                        Severity = ViolationSeverity.High
                    }
                }
            },
            ToolValidation = new ToolTestResult
            {
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = "MCP.GUIDELINE.HINT",
                        Category = "Guidance",
                        Component = "tool",
                        Source = ValidationRuleSource.Guideline,
                        Severity = ValidationFindingSeverity.High,
                        Summary = "Guideline finding."
                    }
                }
            },
            SecurityTesting = new SecurityTestResult
            {
                Vulnerabilities = new List<SecurityVulnerability>
                {
                    new()
                    {
                        Id = "MCP.HEURISTIC.WARNING",
                        Category = "Security",
                        AffectedComponent = "tool",
                        Severity = VulnerabilitySeverity.Critical,
                        Description = "Heuristic warning."
                    }
                }
            }
        };

        var assessment = ValidationVerdictEngine.Calculate(result);

        assessment.BlockingDecisions.Select(decision => decision.Authority).Should().ContainInOrder(
            ValidationRuleSource.Spec,
            ValidationRuleSource.Guideline,
            ValidationRuleSource.Heuristic,
            ValidationRuleSource.Unspecified);
    }
}
