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
    public void Evaluate_AdvisoryMode_WithCoverageLimitedPartialResult_ShouldReturnSuccessExitCode()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.PartiallyCompleted,
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Inventory,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "coverage:protocol-core:json-rpc",
                        Subject = "json-rpc",
                        Dimension = AssuranceDimension.CoreProtocol,
                        RequirementLevel = AssuranceRequirementLevel.Policy,
                        Authority = AssuranceAuthority.ProviderPolicy,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.Incomplete,
                        Summary = "Active probes were not run in safe mode."
                    }
                ],
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "inventory",
                    Status = CertificateEligibilityStatus.ReviewRequired
                }
            }
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "json-rpc",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Reason = "Active probes were not run in safe mode."
        });
        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Advisory);

        outcome.Passed.Should().BeTrue();
        outcome.RecommendedExitCode.Should().Be(0);
        outcome.BlockingSignalCount.Should().Be(0);
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
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Ci,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "claim:should-description",
                        Subject = "tools/list",
                        Dimension = AssuranceDimension.CapabilityContract,
                        RequirementLevel = AssuranceRequirementLevel.Should,
                        Authority = AssuranceAuthority.OfficialSpecification,
                        Source = new AssuranceSourceReference { Revision = "2026-07-28" },
                        Outcome = AssuranceClaimOutcome.Violated,
                        Summary = "SHOULD requirement failed: Tool descriptions should be clear."
                    }
                ],
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "ci",
                    Status = CertificateEligibilityStatus.Ineligible
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Strict);

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().Contain(reason => reason.Contains("SHOULD requirement failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_BalancedMode_WithMayOnlyTierFailures_ShouldRemainPassing()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Inventory,
                Claims =
                [
                    new()
                    {
                        ClaimId = "optional:logging",
                        Subject = "capabilities",
                        Dimension = AssuranceDimension.CapabilityContract,
                        RequirementLevel = AssuranceRequirementLevel.May,
                        Authority = AssuranceAuthority.Heuristic,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.ReviewRequired,
                        Summary = "Optional logging capability was not observed."
                    }
                ],
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "inventory",
                    Status = CertificateEligibilityStatus.ReviewRequired
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Balanced);

        outcome.Passed.Should().BeTrue();
        outcome.BlockingSignalCount.Should().Be(0);
        result.Assurance.Claims.Should().ContainSingle(claim => claim.RequirementLevel == AssuranceRequirementLevel.May);
    }

    [Fact]
    public void Evaluate_BalancedMode_WithLowConfidenceCoveredEvidence_ShouldLeaveDebtToAssuranceExit()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Inventory,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "coverage:tool-surface:tools-list",
                        Subject = "tools/list",
                        Dimension = AssuranceDimension.CapabilityContract,
                        RequirementLevel = AssuranceRequirementLevel.Policy,
                        Authority = AssuranceAuthority.ProviderPolicy,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.Incomplete,
                        Confidence = EvidenceConfidenceLevel.Low,
                        Summary = "Only a parser-boundary response was available."
                    }
                ],
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "inventory",
                    Status = CertificateEligibilityStatus.ReviewRequired
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Balanced);

        result.Assurance.Claims.Should().ContainSingle(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete);
        outcome.Passed.Should().BeTrue();
        outcome.BlockingSignalCount.Should().Be(0);
    }

    [Fact]
    public void Evaluate_StrictMode_WithTrustedCleanResult_ShouldPass()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            Assurance = CreateSuccessfulAssurance()
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
    public void AssessSuppressions_ShouldSeparateActiveExpiredAndMalformedEntries()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var policy = new ValidationPolicyConfig
        {
            Suppressions =
            [
                new ValidationPolicySuppression
                {
                    Id = "active",
                    Owner = "security",
                    Reason = "Temporary exception.",
                    ExpiresOn = now.AddHours(1),
                    RuleId = "RULE.ACTIVE"
                },
                new ValidationPolicySuppression
                {
                    Id = "expired",
                    Owner = "security",
                    Reason = "Expired exception.",
                    ExpiresOn = now,
                    RuleId = "RULE.EXPIRED"
                },
                new ValidationPolicySuppression
                {
                    Id = "missing-owner",
                    Reason = "Invalid.",
                    ExpiresOn = now.AddHours(1),
                    RuleId = "RULE.INVALID"
                }
            ]
        };

        var assessment = ValidationPolicyEvaluator.AssessSuppressions(policy, now);

        assessment.ActiveCount.Should().Be(1);
        assessment.InvalidSuppressions.Should().HaveCount(2);
        assessment.InvalidSuppressions.Select(item => item.Id).Should().BeEquivalentTo("expired", "missing-owner");
    }

    [Fact]
    public void Evaluate_AdvisoryMode_WithExecutionIntegrityFailure_ShouldNotAllowSuppression()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Error
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
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Ci,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "claim:tool-hint-conflict",
                        Subject = "tools",
                        Dimension = AssuranceDimension.CapabilityContract,
                        RequirementLevel = AssuranceRequirementLevel.Should,
                        Authority = AssuranceAuthority.ProviderPolicy,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.ReviewRequired,
                        Summary = "MCP.GUIDELINE.TOOL.HINT_CONFLICT: affected 5/5 component(s) (100%)."
                    }
                ],
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "ci",
                    Status = CertificateEligibilityStatus.Ineligible
                }
            }
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, ValidationPolicyModes.Strict);

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().ContainSingle(reason => reason.Contains("5/5 component(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void RegressionOnlyPolicy_WithoutBaseline_ShouldFailClosed()
    {
        var result = CreateResultWithCriticalFinding();

        var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        {
            RegressionOnly = true
        });

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().ContainSingle(reason => reason.Contains("requires a compatible baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void RegressionOnlyPolicy_WithoutNewBlockers_ShouldPass()
    {
        var result = CreateResultWithCriticalFinding();
        result.BaselineComparison = new AssuranceBaselineComparison
        {
            BaselineValidationId = "baseline",
            ProfileId = "provider",
            ProfileRevision = "test",
            IsRegression = false
        };

        ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        {
            RegressionOnly = true
        }).Passed.Should().BeTrue();
    }

    [Fact]
    public void RegressionOnlyPolicy_WithAssessmentDegradation_ShouldBlock()
    {
        var result = CreateResultWithCriticalFinding();
        result.BaselineComparison = new AssuranceBaselineComparison
        {
            BaselineValidationId = "baseline",
            ProfileId = "provider",
            ProfileRevision = "test",
            CoreConformanceBefore = CoreConformanceStatus.Conformant,
            CoreConformanceAfter = CoreConformanceStatus.Incomplete,
            IsRegression = true
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        {
            RegressionOnly = true
        });

        outcome.Passed.Should().BeFalse();
        outcome.Reasons.Should().ContainSingle(reason => reason.Contains("assessments degraded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegressionOnlyPolicy_WithGovernedWaiver_ShouldSuppressMatchingNewBlocker()
    {
        var result = CreateResultWithCriticalFinding();
        result.BaselineComparison = new AssuranceBaselineComparison
        {
            BaselineValidationId = "baseline",
            ProfileId = "provider",
            ProfileRevision = "test",
            IsRegression = true,
            NewNonSuccessClaims =
            [
                new BaselineClaimChange("TEST.CRITICAL", AssuranceDimension.Security, AssuranceAuthority.DeterministicSecurity, AssuranceClaimOutcome.Violated, "delete_repo")
            ]
        };
        var policy = new ValidationPolicyConfig
        {
            RegressionOnly = true,
            Suppressions =
            [
                new ValidationPolicySuppression
                {
                    Id = "waiver-1",
                    RuleId = "TEST.CRITICAL",
                    Component = "delete_repo",
                    Owner = "security-team",
                    Reason = "Accepted until replacement rollout completes.",
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
                }
            ]
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().BeTrue();
        outcome.AppliedWaivers.Should().ContainSingle(waiver => waiver.Id == "waiver-1");
    }

    private static ValidationResult CreateResultWithCriticalFinding()
    {
        return new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            Assurance = new AssuranceEvaluation
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Ci,
                Claims =
                [
                    new AssuranceClaim
                    {
                        ClaimId = "claim:critical",
                        Subject = "delete_repo",
                        Dimension = AssuranceDimension.Security,
                        RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                        Authority = AssuranceAuthority.DeterministicSecurity,
                        Source = new AssuranceSourceReference { Revision = "test" },
                        Outcome = AssuranceClaimOutcome.Violated,
                        Confidence = EvidenceConfidenceLevel.High,
                        Summary = "Critical destructive issue detected.",
                        Metadata = new Dictionary<string, string> { ["ruleId"] = "TEST.CRITICAL" }
                    }
                ],
                Assessments = new AssuranceAssessmentSet
                {
                    CoreConformance = new CoreConformanceAssessment { Status = CoreConformanceStatus.Conformant },
                    SecurityAssurance = new SecurityAssuranceAssessment { Status = SecurityAssuranceStatus.IssuesFound },
                    OperationalReadiness = new OperationalReadinessAssessment { Status = OperationalReadinessStatus.Ready }
                },
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = "ci",
                    Status = CertificateEligibilityStatus.Ineligible
                }
            }
        };
    }

    private static AssuranceEvaluation CreateSuccessfulAssurance() => new()
    {
        Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Ci,
        Assessments = new AssuranceAssessmentSet
        {
            CoreConformance = new CoreConformanceAssessment { Status = CoreConformanceStatus.Conformant },
            SecurityAssurance = new SecurityAssuranceAssessment { Status = SecurityAssuranceStatus.Verified },
            OperationalReadiness = new OperationalReadinessAssessment { Status = OperationalReadinessStatus.Ready }
        },
        CertificateEligibility = new CertificateEligibilityAssessment
        {
            ProfileId = "ci",
            Status = CertificateEligibilityStatus.Ineligible
        }
    };
}
