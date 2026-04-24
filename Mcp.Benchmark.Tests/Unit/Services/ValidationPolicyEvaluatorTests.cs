using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_AdvisoryMode_WithValidationFailures_ShouldReturnSuccessExitCode()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Failed,
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L2_Caution
            },
            ToolValidation = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = "TEST.CRITICAL",
                                Category = "Security",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.Critical,
                                Summary = "Critical destructive issue detected."
                            }
                        }
                    }
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Advisory);

        outcome.Mode.Should().Be(ValidationPolicyModes.Advisory);
        outcome.Passed.Should().BeTrue();
        outcome.RecommendedExitCode.Should().Be(0);
        outcome.Summary.Should().Contain("Advisory");
    }

    [Fact]
    public void Evaluate_BalancedMode_WithCriticalFinding_ShouldFail()
    {
        var result = CreateResultWithCriticalFinding();

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Balanced);

        outcome.Passed.Should().BeFalse();
        outcome.RecommendedExitCode.Should().Be(1);
        outcome.BlockingSignalCount.Should().Be(1);
        outcome.UnsuppressedSignalCount.Should().Be(1);
        outcome.Reasons.Should().Contain(reason => reason.Contains("Critical destructive issue detected.", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_StrictMode_WithShouldFailures_ShouldFail()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L4_Trusted,
                TierChecks = new List<ComplianceTierCheck>
                {
                    new()
                    {
                        Tier = "SHOULD",
                        Requirement = "Tool descriptions should be clear.",
                        Passed = false,
                        Component = "tools/list",
                        Detail = "description missing"
                    }
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Strict);

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().Contain(reason => reason.Contains("SHOULD requirement failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_StrictMode_WithTrustedCleanResult_ShouldPass()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L4_Trusted,
                TierChecks = new List<ComplianceTierCheck>()
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Strict);

        outcome.Passed.Should().BeTrue();
        outcome.RecommendedExitCode.Should().Be(0);
    }

    [Fact]
    public void Evaluate_BalancedMode_WithActiveSuppression_ShouldSuppressBlockingSignal()
    {
        var result = CreateResultWithCriticalFinding();
        var policy = new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Balanced,
            Suppressions = new List<ValidationPolicySuppression>
            {
                new()
                {
                    Id = "suppress-critical-tool-finding",
                    RuleId = "TEST.CRITICAL",
                    Owner = "navalerakesh",
                    Reason = "Accepted temporarily during rollout.",
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().BeTrue();
        outcome.RecommendedExitCode.Should().Be(0);
        outcome.SuppressedSignalCount.Should().Be(1);
        outcome.UnsuppressedSignalCount.Should().Be(0);
        outcome.BlockingSignalCount.Should().Be(0);
        outcome.AppliedSuppressions.Should().ContainSingle();
        outcome.AppliedSuppressions[0].Id.Should().Be("suppress-critical-tool-finding");
        outcome.AppliedSuppressions[0].MatchedSignalCount.Should().Be(1);
    }

    [Fact]
    public void Evaluate_BalancedMode_WithExpiredSuppression_ShouldIgnoreSuppressionAndFail()
    {
        var result = CreateResultWithCriticalFinding();
        var policy = new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Balanced,
            Suppressions = new List<ValidationPolicySuppression>
            {
                new()
                {
                    Id = "expired-critical-tool-finding",
                    RuleId = "TEST.CRITICAL",
                    Owner = "navalerakesh",
                    Reason = "Expired exception.",
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(-1)
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().BeFalse();
        outcome.AppliedSuppressions.Should().BeEmpty();
        outcome.IgnoredSuppressions.Should().ContainSingle();
        outcome.IgnoredSuppressions[0].Id.Should().Be("expired-critical-tool-finding");
        outcome.IgnoredSuppressions[0].Reason.Should().ContainEquivalentOf("expired");
    }

    [Fact]
    public void Evaluate_BalancedMode_WithInvalidSuppression_ShouldIgnoreSuppressionAndFail()
    {
        var result = CreateResultWithCriticalFinding();
        var policy = new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Balanced,
            Suppressions = new List<ValidationPolicySuppression>
            {
                new()
                {
                    Id = "invalid-critical-tool-finding",
                    RuleId = "TEST.CRITICAL",
                    Reason = "Missing owner should invalidate this suppression.",
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().BeFalse();
        outcome.AppliedSuppressions.Should().BeEmpty();
        outcome.IgnoredSuppressions.Should().ContainSingle();
        outcome.IgnoredSuppressions[0].Reason.Should().ContainEquivalentOf("owner");
    }

    [Fact]
    public void Evaluate_AdvisoryMode_WithExecutionIntegrityFailure_ShouldNotAllowSuppression()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Error,
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.Unknown
            }
        };
        var policy = new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Advisory,
            Suppressions = new List<ValidationPolicySuppression>
            {
                new()
                {
                    Id = "ignore-run-integrity",
                    SignalId = "POLICY.RUN.INTEGRITY",
                    Owner = "navalerakesh",
                    Reason = "Should not apply to execution integrity.",
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().BeFalse();
        outcome.AppliedSuppressions.Should().BeEmpty();
        outcome.Reasons.Should().Contain(reason => reason.Contains("did not complete cleanly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_StrictMode_WithRepeatedCatalogGuidelineFindings_ShouldGroupReasonsByCoverage()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            ToolValidation = new ToolTestResult
            {
                ToolsDiscovered = 5,
                ToolResults = Enumerable.Range(1, 5)
                    .Select(index => new IndividualToolResult
                    {
                        ToolName = $"delete_repo_{index}",
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineHintConflict,
                                Category = "McpGuideline",
                                Component = $"delete_repo_{index}",
                                Severity = ValidationFindingSeverity.High,
                                Summary = $"Tool 'delete_repo_{index}' declares conflicting annotations: readOnlyHint=true and destructiveHint=true."
                            }
                        }
                    })
                    .ToList()
            },
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L4_Trusted,
                TierChecks = new List<ComplianceTierCheck>()
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Strict);

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().ContainSingle(reason => reason.Contains("5/5 component(s)", StringComparison.Ordinal));
    }

    private static ValidationResult CreateResultWithCriticalFinding()
    {
        return new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            ToolValidation = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = "TEST.CRITICAL",
                                Category = "Security",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.Critical,
                                Summary = "Critical destructive issue detected."
                            }
                        }
                    }
                }
            },
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L3_Acceptable,
                TierChecks = new List<ComplianceTierCheck>()
            }
        };
    }
}
