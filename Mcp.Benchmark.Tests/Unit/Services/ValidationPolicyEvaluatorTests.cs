using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationPolicyEvaluatorTests
{
    [Fact]
    public void SelectedScopeClassifier_ShouldRejectMissingInputs()
    {
        FluentActions.Invoking(() => ValidationPolicyEvaluator.IsOutsideSelectedScope(null!, null!))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("assurance");
        FluentActions.Invoking(() => ValidationPolicyEvaluator.IsOutsideSelectedScope(new ValidationResult().Assurance, null!))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("claim");
    }

    private static readonly DateTimeOffset PolicyClock = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("core", false)]
    [InlineData("core", true)]
    [InlineData("security", false)]
    [InlineData("security", true)]
    [InlineData("operations", false)]
    [InlineData("operations", true)]
    [InlineData("certificate", false)]
    [InlineData("certificate", true)]
    public void RegressionPolicy_ShouldNotLetOptionalClaimsHideAssessmentDegradation(string degraded, bool optionalClaim)
    {
        ValidationResult Create(bool current) => new()
        {
            OverallStatus = ValidationStatus.Passed, EndTime = PolicyClock.UtcDateTime,
            Assurance = new()
            {
                Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Comprehensive,
                RequiredExtensionIds = [],
                Claims = current && optionalClaim ? [new AssuranceClaim
                {
                    ClaimId = "optional-new", Subject = "unselected-extension", Dimension = AssuranceDimension.Extension,
                    RequirementLevel = AssuranceRequirementLevel.Must, Authority = AssuranceAuthority.OfficialSpecification,
                    Outcome = AssuranceClaimOutcome.Violated, Summary = "Optional failure", Source = new() { Revision = "fixture" }
                }] : [],
                Assessments = new()
                {
                    CoreConformance = new() { Status = current && degraded == "core" ? CoreConformanceStatus.Incomplete : CoreConformanceStatus.Conformant },
                    SecurityAssurance = new() { Status = current && degraded == "security" ? SecurityAssuranceStatus.Incomplete : SecurityAssuranceStatus.Verified },
                    OperationalReadiness = new() { Status = current && degraded == "operations" ? OperationalReadinessStatus.Incomplete : OperationalReadinessStatus.Ready }
                },
                CertificateEligibility = new()
                {
                    ProfileId = "comprehensive",
                    Status = degraded == "certificate" && !current ? CertificateEligibilityStatus.ReviewRequired : CertificateEligibilityStatus.Ineligible
                }
            }
        };
        var baseline = Create(false);
        var current = Create(true);
        current.BaselineComparison = ValidationBaselineEvaluator.Compare(baseline, current);
        var outcome = ValidationPolicyEvaluator.Evaluate(current, new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Strict, RegressionOnly = true
        });
        outcome.Passed.Should().Be(degraded == "none");
        outcome.BlockingSignalCount.Should().Be(degraded == "none" ? 0 : 1);
        if (degraded != "none")
            outcome.Reasons.Should().Equal("One or more independent assurance assessments degraded relative to baseline.");
        current.Assurance.Claims.Should().HaveCount(optionalClaim ? 1 : 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void EmptyPolicyMode_ShouldUseBalancedAndNullSuppressionInventoryShouldStayEmpty(string? mode)
    {
        ValidationPolicyEvaluator.NormalizeMode(mode).Should().Be(ValidationPolicyModes.Balanced);
        var assessment = ValidationPolicyEvaluator.AssessSuppressions(null, PolicyClock);
        assessment.ActiveCount.Should().Be(0);
        assessment.InvalidSuppressions.Should().BeEmpty();
        FluentActions.Invoking(() => ValidationPolicyEvaluator.Evaluate(null!, new ValidationPolicyConfig()))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("owner", "Missing owner metadata.")]
    [InlineData("reason", "Missing reason metadata.")]
    [InlineData("expiry", "Missing expiresOn metadata.")]
    [InlineData("selectors-null", "Suppression has no selectors.")]
    [InlineData("selectors-empty", "Suppression has no selectors.")]
    [InlineData("selectors-space", "Suppression has no selectors.")]
    public void InvalidSuppression_ShouldRetainIdentityAndExactRejectionReason(string variant, string reason)
    {
        var waiver = BoundaryWaiver();
        waiver.Id = "rejected";
        if (variant == "owner") waiver.Owner = " ";
        if (variant == "reason") waiver.Reason = " ";
        if (variant == "expiry") waiver.ExpiresOn = null;
        if (variant.StartsWith("selectors-", StringComparison.Ordinal))
        {
            var value = variant == "selectors-null" ? null : variant == "selectors-empty" ? "" : " \t";
            waiver.SignalId = waiver.RuleId = waiver.Component = waiver.Source = waiver.Category = value;
        }
        var policy = new ValidationPolicyConfig { Suppressions = [waiver] };
        var assessment = ValidationPolicyEvaluator.AssessSuppressions(policy, PolicyClock);
        assessment.ActiveCount.Should().Be(0);
        assessment.InvalidSuppressions.Should().ContainSingle();
        assessment.InvalidSuppressions[0].Id.Should().Be("rejected");
        assessment.InvalidSuppressions[0].Reason.Should().Be(reason);
        var result = CreateResultWithCriticalFinding();
        result.EndTime = PolicyClock.UtcDateTime;
        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);
        outcome.Passed.Should().BeFalse();
        outcome.IgnoredSuppressions.Should().ContainSingle();
        outcome.IgnoredSuppressions[0].Reason.Should().Be(reason);
    }

    public static IEnumerable<object[]> SelectorCases() =>
        from selector in new[] { "signal", "rule", "component", "source", "category" }
        from value in new[] { "match", "mismatch", "empty", "space" }
        select new object[] { selector, value };

    [Theory]
    [MemberData(nameof(SelectorCases))]
    public void SuppressionSelectors_ShouldMatchConjunctivelyAndIgnoreAbsentSelectors(string selector, string variation)
    {
        var result = CreateResultWithCriticalFinding();
        result.EndTime = PolicyClock.UtcDateTime;
        var waiver = BoundaryWaiver();
        waiver.Component = "delete_repo";
        waiver.RuleId = selector == "component" ? "TEST.CRITICAL" : null;
        var value = variation switch
        {
            "empty" => "", "space" => " \t", "mismatch" => "different",
            _ => selector switch
            {
                "signal" => "policy.claim.claim_critical", "rule" => "test.critical", "component" => "DELETE_REPO",
                "source" => "deterministicsecurity", _ => "security"
            }
        };
        switch (selector)
        {
            case "signal": waiver.SignalId = value; break;
            case "rule": waiver.RuleId = value; break;
            case "component": waiver.Component = value; break;
            case "source": waiver.Source = value; break;
            case "category": waiver.Category = value; break;
        }
        var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Suppressions = [waiver] });
        outcome.Passed.Should().Be(variation != "mismatch");
        outcome.TotalSignalCount.Should().Be(1);
        outcome.IgnoredSuppressions.Should().BeEmpty();
        if (variation == "mismatch")
        {
            outcome.AppliedSuppressions.Should().BeEmpty();
            outcome.BlockingSignalCount.Should().Be(1);
        }
        else
        {
            var applied = outcome.AppliedSuppressions.Should().ContainSingle().Subject;
            applied.MatchedSignalCount.Should().Be(1);
            applied.MatchedSignals.Should().Equal("Critical destructive issue detected.");
            applied.Owner.Should().Be("fixture-owner");
            applied.Reason.Should().Be("Reviewed temporary exception.");
            applied.ExpiresOn.Should().Be(PolicyClock.AddHours(1));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void SuppressionIdentity_ShouldUseOnlyPresentSelectorsWhenIdIsAbsent(string empty)
    {
        var waiver = BoundaryWaiver();
        waiver.Id = empty;
        waiver.SignalId = "POLICY.CLAIM.CLAIM_CRITICAL";
        waiver.RuleId = empty;
        waiver.Component = "delete_repo";
        waiver.Source = empty;
        waiver.Category = "Security";
        var result = CreateResultWithCriticalFinding();
        result.EndTime = PolicyClock.UtcDateTime;
        result.Assurance.Claims.Single().Metadata["ruleId"] = empty;
        var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Suppressions = [waiver] });
        outcome.Passed.Should().BeTrue();
        outcome.AppliedSuppressions.Single().Id.Should().Be("POLICY.CLAIM.CLAIM_CRITICAL|delete_repo|Security");
        waiver.SignalId = empty;
        waiver.RuleId = "claim:critical";
        ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Suppressions = [waiver] }).Passed.Should().BeTrue();
    }

    [Theory]
    [InlineData(AssuranceDimension.CoreProtocol, false, "Core conformance is Nonconformant.")]
    [InlineData(AssuranceDimension.CoreProtocol, true, "Core conformance is Incomplete.")]
    [InlineData(AssuranceDimension.Security, false, "Security assurance is IssuesFound.")]
    [InlineData(AssuranceDimension.Security, true, "Security assurance is Incomplete.")]
    [InlineData(AssuranceDimension.Operations, false, "Operational readiness is NotReady.")]
    [InlineData(AssuranceDimension.Operations, true, "Operational readiness is Incomplete.")]
    public void AssessmentFallback_ShouldRemainVisibleAndUnsuppressible(AssuranceDimension dimension, bool incomplete, string reason)
    {
        var assessments = new AssuranceAssessmentSet
        {
            CoreConformance = new() { Status = dimension == AssuranceDimension.CoreProtocol
                ? incomplete ? CoreConformanceStatus.Incomplete : CoreConformanceStatus.Nonconformant : CoreConformanceStatus.Conformant },
            SecurityAssurance = new() { Status = dimension == AssuranceDimension.Security
                ? incomplete ? SecurityAssuranceStatus.Incomplete : SecurityAssuranceStatus.IssuesFound : SecurityAssuranceStatus.Verified },
            OperationalReadiness = new() { Status = dimension == AssuranceDimension.Operations
                ? incomplete ? OperationalReadinessStatus.Incomplete : OperationalReadinessStatus.NotReady : OperationalReadinessStatus.Ready }
        };
        var result = BoundaryResult([BoundaryClaim("unrelated", AssuranceClaimOutcome.ReviewRequired, AssuranceDimension.ClientCompatibility)], assessments);
        var waiver = BoundaryWaiver();
        waiver.Component = "assurance-assessment";
        foreach (var mode in new[] { ValidationPolicyModes.Balanced, ValidationPolicyModes.Strict })
        {
            var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Mode = mode, Suppressions = [waiver] });
            outcome.Passed.Should().Be(incomplete && mode == ValidationPolicyModes.Balanced);
            outcome.TotalSignalCount.Should().Be(2);
            outcome.SuppressedSignalCount.Should().Be(0);
            if (!incomplete || mode == ValidationPolicyModes.Strict) outcome.Reasons.Should().Contain(reason);
        }
    }

    [Theory]
    [InlineData(false, "run")]
    [InlineData(true, "run")]
    [InlineData(false, "critical")]
    [InlineData(true, "baseline")]
    public void IntegrityFailures_ShouldNotBeWaivableInAdvisoryMode(bool regression, string failure)
    {
        var result = BoundaryResult([]);
        var waiver = BoundaryWaiver();
        waiver.Component = "validation-run";
        if (failure == "run") result.OverallStatus = ValidationStatus.Cancelled;
        if (failure == "critical") result.CriticalErrors.Add("Fixture collection failed.");
        if (failure != "baseline") result.BaselineComparison = new() { ProfileId = "ci", ProfileRevision = "fixture" };
        var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        { Mode = ValidationPolicyModes.Advisory, RegressionOnly = regression, Suppressions = [waiver] });
        outcome.Passed.Should().BeFalse();
        outcome.BlockingSignalCount.Should().Be(1);
        outcome.SuppressedSignalCount.Should().Be(0);
        outcome.Reasons.Should().Equal(failure switch
        {
            "run" => "Validation run did not complete cleanly: Cancelled.",
            "critical" => "Critical error recorded: Fixture collection failed.",
            _ => "Regression-only policy requires a compatible baseline validation result."
        });
    }

    [Theory]
    [InlineData(AssuranceClaimOutcome.Violated, true)]
    [InlineData(AssuranceClaimOutcome.Error, true)]
    [InlineData(AssuranceClaimOutcome.Incomplete, false)]
    [InlineData(AssuranceClaimOutcome.ReviewRequired, false)]
    public void RegressionSignals_ShouldRetainExactReasonAndSeverity(AssuranceClaimOutcome state, bool balancedBlocked)
    {
        var result = BoundaryResult([BoundaryClaim("new.claim", state)]);
        result.BaselineComparison = new()
        {
            ProfileId = "ci", ProfileRevision = "fixture", IsRegression = true,
            NewNonSuccessClaims = [new("new.claim", AssuranceDimension.Security, AssuranceAuthority.DeterministicSecurity, state, "fixture")]
        };
        foreach (var mode in new[] { ValidationPolicyModes.Balanced, ValidationPolicyModes.Strict })
        {
            var outcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Mode = mode, RegressionOnly = true });
            outcome.Passed.Should().Be(mode == ValidationPolicyModes.Balanced && !balancedBlocked);
            if (!outcome.Passed) outcome.Reasons.Should().Equal($"New non-success assurance claim relative to baseline: new.claim ({state}).");
        }
        var waiver = BoundaryWaiver();
        waiver.SignalId = "POLICY.BASELINE.NEW_CLAIM";
        ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        { Mode = ValidationPolicyModes.Strict, RegressionOnly = true, Suppressions = [waiver] }).Passed.Should().BeTrue();
    }

    [Fact]
    public void PolicySummariesAndWaiverOrdering_ShouldReflectExactRetainedCounts()
    {
        var result = BoundaryResult([BoundaryClaim("first.claim"), BoundaryClaim("second.claim")]);
        var first = BoundaryWaiver(); first.Id = "z-last"; first.SignalId = "POLICY.CLAIM.FIRST_CLAIM";
        var second = BoundaryWaiver(); second.Id = "A-first"; second.SignalId = "POLICY.CLAIM.SECOND_CLAIM";
        var policy = new ValidationPolicyConfig();
        ValidationPolicyEvaluator.Evaluate(result, policy).Summary.Should().Be("Balanced policy blocked the validation result with 2 blocking signal(s).");
        policy.Suppressions.Add(first);
        var partial = ValidationPolicyEvaluator.Evaluate(result, policy);
        partial.Summary.Should().Be("Balanced policy blocked the validation result with 1 blocking signal(s) after suppressing 1.");
        partial.SuppressedSignalCount.Should().Be(1);
        partial.BlockingSignalCount.Should().Be(1);
        policy.Suppressions.Add(second);
        var all = ValidationPolicyEvaluator.Evaluate(result, policy);
        all.Summary.Should().Be("Balanced policy requirements satisfied after suppressing 2 signal(s).");
        all.AppliedSuppressions.Select(waiver => waiver.Id).Should().Equal("A-first", "z-last");
        ValidationPolicyEvaluator.Evaluate(BoundaryResult([]), policy).Summary.Should().Be("Balanced policy requirements satisfied.");
        result.BaselineComparison = new() { ProfileId = "ci", ProfileRevision = "fixture", IsRegression = true };
        var degradation = BoundaryWaiver();
        degradation.SignalId = "POLICY.BASELINE.ASSESSMENT_DEGRADED";
        degradation.Component = "assurance-assessments";
        degradation.Category = "Baseline";
        ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { RegressionOnly = true, Suppressions = [degradation] })
            .AppliedSuppressions.Should().ContainSingle();
    }

    private static ValidationPolicySuppression BoundaryWaiver() => new()
    {
        Id = "fixture", Owner = " fixture-owner ", Reason = " Reviewed temporary exception. ", ExpiresOn = PolicyClock.AddHours(1)
    };

    private static AssuranceClaim BoundaryClaim(string id, AssuranceClaimOutcome outcome = AssuranceClaimOutcome.Violated,
        AssuranceDimension dimension = AssuranceDimension.Security) => new()
    {
        ClaimId = id, Subject = "fixture", Dimension = dimension, RequirementLevel = AssuranceRequirementLevel.SecurityControl,
        Authority = AssuranceAuthority.DeterministicSecurity, Source = new() { Revision = "fixture" }, Outcome = outcome, Summary = id
    };

    private static ValidationResult BoundaryResult(IReadOnlyList<AssuranceClaim> claims, AssuranceAssessmentSet? assessments = null) => new()
    {
        OverallStatus = ValidationStatus.Passed, EndTime = PolicyClock.UtcDateTime,
        Assurance = new()
        {
            Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Ci, Claims = claims,
            Assessments = assessments ?? new()
            {
                CoreConformance = new() { Status = CoreConformanceStatus.Conformant },
                SecurityAssurance = new() { Status = SecurityAssuranceStatus.Verified },
                OperationalReadiness = new() { Status = OperationalReadinessStatus.Ready }
            },
            CertificateEligibility = new() { ProfileId = "ci", Status = CertificateEligibilityStatus.Ineligible }
        }
    };

    [Theory]
    [InlineData(ValidationPolicyModes.Balanced, false, false, false)]
    [InlineData(ValidationPolicyModes.Strict, false, false, false)]
    [InlineData(ValidationPolicyModes.Balanced, true, false, true)]
    [InlineData(ValidationPolicyModes.Strict, true, false, true)]
    [InlineData(ValidationPolicyModes.Balanced, false, true, true)]
    public void ComprehensivePolicy_ShouldRetainOptionalClaimsWithoutMakingThemRequired(string mode, bool selectedExtension,
        bool requiredOfficial, bool blocked)
    {
        var claims = new AssuranceClaim[]
        {
            new()
            {
                ClaimId = "optional-external", Subject = "fixture-scenario", Dimension = AssuranceDimension.ExternalConformance,
                RequirementLevel = requiredOfficial ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.May,
                Authority = AssuranceAuthority.OfficialConformance, Source = new() { Revision = "fixture" },
                Outcome = AssuranceClaimOutcome.Violated, Summary = "External failure remains visible."
            },
            new()
            {
                ClaimId = "optional-extension", Subject = "fixture-extension", Dimension = AssuranceDimension.Extension,
                RequirementLevel = AssuranceRequirementLevel.Must, Authority = AssuranceAuthority.OfficialSpecification,
                Source = new() { Revision = "fixture" }, Outcome = AssuranceClaimOutcome.Violated, Summary = "Extension failure remains visible."
            }
        };
        var result = new ValidationResult { OverallStatus = ValidationStatus.Passed, Assurance = new()
        {
            Profile = Mcp.Benchmark.Core.Constants.AssuranceProfileCatalog.Comprehensive,
            CertificateEligibility = new() { ProfileId = "comprehensive", Status = CertificateEligibilityStatus.Ineligible },
            RequiredExtensionIds = selectedExtension ? ["fixture-extension"] : [],
            Claims = claims,
            Assessments = new()
            {
                CoreConformance = new() { Status = CoreConformanceStatus.Conformant },
                SecurityAssurance = new() { Status = SecurityAssuranceStatus.Verified },
                OperationalReadiness = new() { Status = OperationalReadinessStatus.Ready }
            }
        } };
        var outcome = ValidationPolicyEvaluator.Evaluate(result, mode);
        outcome.Passed.Should().Be(!blocked);
        outcome.TotalSignalCount.Should().Be(2);
        outcome.SuppressedSignalCount.Should().Be(0);
        result.Assurance.Claims.Should().BeSameAs(claims);
        result.Assurance.Claims.Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Violated);
        result.BaselineComparison = new AssuranceBaselineComparison
        {
            ProfileId = "comprehensive", ProfileRevision = result.Assurance.Profile.Revision, IsRegression = true,
            NewNonSuccessClaims = claims.Select(claim => new BaselineClaimChange(
                claim.ClaimId, claim.Dimension, claim.Authority, claim.Outcome, claim.Subject)).ToArray()
        };
        var regression = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Mode = mode, RegressionOnly = true });
        regression.Passed.Should().Be(!blocked);
        regression.TotalSignalCount.Should().Be(2);
        result.CloneWithoutSecrets().Assurance.Claims.Should().HaveCount(2)
            .And.OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Violated);
    }

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

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Evaluate_CompletedResult_ShouldUseRecordedAssessmentTime(int expiryOffsetSeconds, bool expectedPass)
    {
        var result = CreateResultWithCriticalFinding();
        var assessedAt = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        result.StartTime = assessedAt.AddMinutes(-1).UtcDateTime;
        result.EndTime = assessedAt.UtcDateTime;
        var policy = new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Balanced,
            Suppressions = [new ValidationPolicySuppression
            {
                Id = "recorded-clock", RuleId = "TEST.CRITICAL", Owner = "fixture-owner",
                Reason = "Frozen evaluation boundary.", ExpiresOn = assessedAt.AddSeconds(expiryOffsetSeconds)
            }]
        };

        var outcome = ValidationPolicyEvaluator.Evaluate(result, policy);

        outcome.Passed.Should().Be(expectedPass);
        outcome.SuppressedSignalCount.Should().Be(expectedPass ? 1 : 0);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc, false)]
    [InlineData(DateTimeKind.Utc, true)]
    [InlineData(DateTimeKind.Local, true)]
    [InlineData(DateTimeKind.Unspecified, true)]
    public void Evaluate_RecordedTime_ShouldNormalizeExplicitZonesAndRejectAmbiguity(DateTimeKind kind, bool completed)
    {
        var result = CreateResultWithCriticalFinding();
        var instant = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var recorded = kind == DateTimeKind.Local ? instant.LocalDateTime : DateTime.SpecifyKind(instant.UtcDateTime, kind);
        result.StartTime = recorded;
        result.EndTime = completed ? recorded : null;
        var policy = new ValidationPolicyConfig
        {
            Suppressions = [new ValidationPolicySuppression
            {
                Id = "timezone-control", RuleId = "TEST.CRITICAL", Owner = "fixture-owner",
                Reason = "Recorded timezone boundary.", ExpiresOn = instant.AddSeconds(1)
            }]
        };

        if (kind == DateTimeKind.Unspecified)
            FluentActions.Invoking(() => ValidationPolicyEvaluator.Evaluate(result, policy)).Should().Throw<ArgumentException>();
        else ValidationPolicyEvaluator.Evaluate(result, policy).Passed.Should().BeTrue();
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
