using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class AssuranceAssessmentEngineTests
{
    [Fact]
    public void Evaluate_NullArguments_ShouldThrow()
    {
        var nullResult = () => AssuranceAssessmentEngine.Evaluate(null!, AssuranceProfileCatalog.Inventory);
        var nullProfile = () => AssuranceAssessmentEngine.Evaluate(new ValidationResult(), null!);

        nullResult.Should().Throw<ArgumentNullException>();
        nullProfile.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_HeuristicReject_ShouldRequireReviewWithoutCoreNonconformance()
    {
        var result = CreateResultWithCoveredBaseline();
        result.VerdictAssessment = new VerdictAssessment();
        result.VerdictAssessment.TriggeredDecisions.Add(new DecisionRecord
        {
            DecisionId = "heuristic-delete",
            RuleId = "MCP.CONTENT.SystemImpact",
            Lane = EvaluationLane.Baseline,
            Authority = ValidationRuleSource.Heuristic,
            Origin = EvidenceOrigin.HeuristicInference,
            Gate = GateOutcome.Reject,
            Severity = ValidationFindingSeverity.High,
            Category = "ContentSafety.SystemImpact",
            Component = "delete_file",
            Summary = "Name suggests system impact.",
            ImpactAreas = [ImpactArea.UnsafeAutonomy]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Claims.Should().ContainSingle(claim =>
            claim.ClaimId == "decision:heuristic-delete" &&
            claim.Authority == AssuranceAuthority.Heuristic &&
            claim.Outcome == AssuranceClaimOutcome.ReviewRequired);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Incomplete);
        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Evaluate_MustViolation_ShouldMakeCoreNonconformant()
    {
        var result = CreateResultWithCoveredBaseline();
        result.TrustAssessment = new McpTrustAssessment
        {
            TierChecks =
            [
                new ComplianceTierCheck
                {
                    Tier = "MUST",
                    Component = "json-rpc",
                    Requirement = "Requests include a non-null ID.",
                    Passed = false
                }
            ]
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Ci, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Nonconformant);
        evaluation.Assessments.CoreConformance.BlockingClaimIds.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_IncompleteMust_ShouldMakeCoreIncomplete()
    {
        var result = CreateResultWithCoveredBaseline();
        result.Assessments.OfficialConformance = CreateOfficialConformance(
            OfficialConformanceVerificationStatus.Incomplete,
            [
                new OfficialConformanceScenarioAssessment
                {
                    ScenarioId = "required",
                    ScoringDisposition = OfficialConformanceScoringDisposition.Required,
                    Checks =
                    [
                        new OfficialConformanceCheckAssessment
                        {
                            Id = "check",
                            Name = "check",
                            Description = "Required check.",
                            Status = OfficialConformanceCheckStatus.Skipped
                        }
                    ]
                }
            ]);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Incomplete);
        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.IncompleteClaimIds.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_ShouldAndExtensionClaims_ShouldNotCountAsRequiredCoreClaims()
    {
        var result = CreateCompleteResult();
        result.TrustAssessment!.TierChecks.Add(new ComplianceTierCheck
        {
            Tier = "SHOULD",
            Component = "optional",
            Requirement = "Recommended behavior.",
            Passed = false
        });
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "example/extension",
            Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "2026-01-01",
            SpecificationSha256 = new string('e', 64),
            Verdict = ValidationVerdict.Reject,
            Findings =
            [
                new ValidationFinding
                {
                    RuleId = "EXT.MUST",
                    Source = ValidationRuleSource.Spec,
                    Component = "extension",
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Extension requirement failed."
                }
            ]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Conformant);
    }

    [Fact]
    public void Evaluate_ProviderProfile_ShouldFailClosedWhenEvidencePrerequisitesAreMissing()
    {
        var result = CreateResultWithCoveredBaseline();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("official conformance", StringComparison.OrdinalIgnoreCase));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("signed attestation", StringComparison.OrdinalIgnoreCase));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("Repeated-run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ClientCompatibilityFailure_ShouldRemainSeparateFromCoreAssessment()
    {
        var result = CreateResultWithCoveredBaseline();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "client-profiles",
            Scope = "incompatible-client",
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Failed,
            Lane = EvaluationLane.ClientCompatibility,
            Confidence = EvidenceConfidenceLevel.High
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.EvidenceSummary.TotalDeclarations.Should().Be(1);
        evaluation.Claims.Should().Contain(claim =>
            claim.Dimension == AssuranceDimension.ClientCompatibility &&
            claim.Outcome == AssuranceClaimOutcome.Violated);
    }

    [Fact]
    public void Evaluate_SatisfiedMustAndDeterministicControls_ShouldProduceIndependentSuccesses()
    {
        var result = CreateCompleteResult();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Conformant);
        evaluation.Assessments.CoreConformance.RequiredClaims.Should().Be(1);
        evaluation.Assessments.CoreConformance.SatisfiedRequiredClaims.Should().Be(1);
        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.SecurityAssurance.DeterministicControlClaims.Should().Be(1);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
        evaluation.Assessments.OperationalReadiness.ObjectiveClaims.Should().Be(1);
    }

    [Theory]
    [InlineData(GateOutcome.Reject, SecurityAssuranceStatus.IssuesFound)]
    [InlineData(GateOutcome.ReviewRequired, SecurityAssuranceStatus.Incomplete)]
    [InlineData(GateOutcome.CoverageDebt, SecurityAssuranceStatus.Incomplete)]
    public void Evaluate_DeterministicSecurityOutcomes_ShouldReduceExactly(
        GateOutcome gate,
        SecurityAssuranceStatus expected)
    {
        var result = CreateCompleteResult();
        result.VerdictAssessment!.TriggeredDecisions.RemoveAll(decision => decision.DecisionId == "security-control");
        result.VerdictAssessment.TriggeredDecisions.Add(CreateDecision("security-control", gate, ImpactArea.AuthenticationBoundary));

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(expected);
        if (expected == SecurityAssuranceStatus.IssuesFound)
            evaluation.Assessments.SecurityAssurance.IssueClaimIds.Should().ContainSingle("decision:security-control");
        else
            evaluation.Assessments.SecurityAssurance.IncompleteClaimIds.Should().ContainSingle("decision:security-control");
    }

    [Theory]
    [InlineData(GateOutcome.Reject, OperationalReadinessStatus.NotReady)]
    [InlineData(GateOutcome.ReviewRequired, OperationalReadinessStatus.Incomplete)]
    [InlineData(GateOutcome.CoverageDebt, OperationalReadinessStatus.Incomplete)]
    public void Evaluate_OperationalOutcomes_ShouldReduceExactly(
        GateOutcome gate,
        OperationalReadinessStatus expected)
    {
        var result = CreateCompleteResult();
        result.VerdictAssessment!.TriggeredDecisions.RemoveAll(decision => decision.DecisionId == "operations-objective");
        result.VerdictAssessment.TriggeredDecisions.Add(CreateDecision("operations-objective", gate, ImpactArea.OperationalResilience));

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.OperationalReadiness.Status.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_SecurityCoverageDebt_ShouldPreventVerifiedStatus()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.RemoveAll(item => item.LayerId == "security-controls");
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "security-controls",
            Scope = "authentication",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_OperationsCoverageDebt_ShouldAddSyntheticIncompleteClaim()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.RemoveAll(item => item.LayerId == "performance");
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "performance",
            Scope = "repeated-measurements",
            Status = ValidationCoverageStatus.Blocked,
            ObservedOutcome = ValidationOutcome.Blocked,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
        evaluation.Assessments.OperationalReadiness.IncompleteClaimIds.Should().Contain("coverage:operations");
    }

    [Fact]
    public void Evaluate_HeuristicReview_ShouldNotDowngradeVerifiedDeterministicSecurity()
    {
        var result = CreateCompleteResult();
        result.VerdictAssessment!.TriggeredDecisions.Add(new DecisionRecord
        {
            DecisionId = "heuristic-review",
            Lane = EvaluationLane.Baseline,
            Authority = ValidationRuleSource.Heuristic,
            Origin = EvidenceOrigin.HeuristicInference,
            Gate = GateOutcome.ReviewRequired,
            Severity = ValidationFindingSeverity.Medium,
            Category = "Security",
            Component = "tool",
            Summary = "Review metadata.",
            ImpactAreas = [ImpactArea.UnsafeAutonomy]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.SecurityAssurance.ReviewClaimCount.Should().Be(1);
    }

    [Fact]
    public void Evaluate_UnrelatedCoverageDebt_ShouldNotDowngradeSecurityOrOperations()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "resource-catalog",
            Scope = "resources/list",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.None
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
    }

    [Fact]
    public void Evaluate_ErrorHandlingCoverageDebt_ShouldDowngradeOperationsOnly()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "error-handling",
            Scope = "invalid-request",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.Low
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
    }

    [Fact]
    public void Evaluate_CertificateThresholds_ShouldTreatExactBoundaryAsSatisfied()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(minimumCoverage: 1, minimumConfidence: 1);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
    }

    [Fact]
    public void Evaluate_CertificateCoverageAndConfidenceDebt_ShouldNameBothReasons()
    {
        var result = CreateCompleteResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "resource-catalog",
            Scope = "resources/list",
            Status = ValidationCoverageStatus.Inconclusive,
            ObservedOutcome = ValidationOutcome.Inconclusive,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.Low
        });
        var profile = CertificateProfile(minimumCoverage: 1, minimumConfidence: 1);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Evidence coverage", StringComparison.Ordinal));
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Evidence confidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RequiredAttestation_ShouldNameMissingAttestationOnly()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresAttestation: true);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard
        });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle("A verified signed attestation is required.");
    }

    [Fact]
    public void Evaluate_HarnessConsistency_ShouldTakePrecedenceOverLegacyScalar()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresRepeatedRuns: true, minimumConsistency: 0.99);
        var context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            RepeatedRunConsistency = 1,
            ProviderHarness = new ProviderHarnessAssessment
            {
                Status = ProviderHarnessStatus.Verified,
                AuthorizationVerified = true,
                TargetIdentitySha256 = new string('a', 64),
                AcceptedRunCount = 3,
                RepeatedRunConsistency = 0.98
            }
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.StartsWith("Repeated-run consistency", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_LegacyConsistencyAtExactThreshold_ShouldSatisfyThresholdButStillRequireHarness()
    {
        var result = CreateCompleteResult();
        var profile = CertificateProfile(requiresRepeatedRuns: true, minimumConsistency: 0.99);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, profile, new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            RepeatedRunConsistency = 0.99
        });

        evaluation.CertificateEligibility.Reasons.Should().ContainSingle(reason => reason.Contains("provider harness", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_CompleteProviderEvidence_ShouldBeEligible()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Standard,
            HasSignedAttestation = true,
            ProviderHarness = new ProviderHarnessAssessment
            {
                Status = ProviderHarnessStatus.Verified,
                AuthorizationVerified = true,
                TargetIdentitySha256 = new string('a', 64),
                AcceptedRunCount = 3,
                RepeatedRunConsistency = 1
            }
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
        evaluation.CertificateEligibility.Reasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OfficialConformanceVerificationStatus.Conflict)]
    [InlineData(OfficialConformanceVerificationStatus.Incomplete)]
    public void Evaluate_NonVerifiedOfficialConformance_ShouldNotBeEligible(
        OfficialConformanceVerificationStatus verificationStatus)
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(verificationStatus);
        var context = CompleteProviderContext();

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(
            verificationStatus == OfficialConformanceVerificationStatus.Conflict
                ? CertificateEligibilityStatus.Ineligible
                : CertificateEligibilityStatus.ReviewRequired);
    }

    [Fact]
    public void Evaluate_InvalidProviderHarness_ShouldBeIneligible()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = CompleteProviderContext(ProviderHarnessStatus.Invalid);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Evaluate_SafeModeForProvider_ShouldRequireReview()
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        var context = CompleteProviderContext();
        context = new AssuranceEvaluationContext
        {
            ExecutionMode = ExecutionMode.Safe,
            HasSignedAttestation = context.HasSignedAttestation,
            ProviderHarness = context.ProviderHarness
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Provider, context);

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().Contain(reason => reason.Contains("below required mode", StringComparison.Ordinal));
    }

    private static ValidationResult CreateResultWithCoveredBaseline()
    {
        var result = new ValidationResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "json-rpc",
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Succeeded,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.High
        });
        return result;
    }

    private static ValidationResult CreateCompleteResult()
    {
        var result = new ValidationResult
        {
            TrustAssessment = new McpTrustAssessment
            {
                TierChecks =
                [
                    new ComplianceTierCheck
                    {
                        Tier = "MUST",
                        Component = "json-rpc",
                        Requirement = "Request IDs are valid.",
                        Passed = true
                    }
                ]
            },
            VerdictAssessment = new VerdictAssessment()
        };
        result.VerdictAssessment.TriggeredDecisions.Add(CreateDecision("security-control", GateOutcome.Note, ImpactArea.AuthenticationBoundary));
        result.VerdictAssessment.TriggeredDecisions.Add(CreateDecision("operations-objective", GateOutcome.Note, ImpactArea.OperationalResilience));
        AddCovered(result, "protocol-core", "json-rpc");
        AddCovered(result, "security-controls", "authentication");
        AddCovered(result, "performance", "repeated-measurements");
        return result;
    }

    private static DecisionRecord CreateDecision(string id, GateOutcome gate, ImpactArea impactArea) => new()
    {
        DecisionId = id,
        Lane = EvaluationLane.Baseline,
        Authority = ValidationRuleSource.Heuristic,
        Origin = EvidenceOrigin.DeterministicObservation,
        Gate = gate,
        Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.High : ValidationFindingSeverity.Info,
        Category = impactArea.ToString(),
        Component = id,
        Summary = $"Decision {id}.",
        ImpactAreas = [impactArea]
    };

    private static void AddCovered(ValidationResult result, string layer, string scope) =>
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = layer,
            Scope = scope,
            Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = ValidationOutcome.Succeeded,
            Lane = EvaluationLane.Baseline,
            Confidence = EvidenceConfidenceLevel.High
        });

    private static OfficialConformanceAssessment CreateOfficialConformance(
        OfficialConformanceVerificationStatus status,
        IReadOnlyList<OfficialConformanceScenarioAssessment>? scenarios = null) => new()
    {
        VerificationStatus = status,
        Runner = new OfficialConformanceRunnerIdentity
        {
            PackageName = "official",
            Version = "1.0.0",
            SourceCommit = new string('a', 40),
            PackageSha256 = new string('b', 64)
        },
        RequirementSet = new OfficialConformanceRequirementSet
        {
            Revision = "2026-07-28",
            Sha256 = new string('c', 64)
        },
        Target = new OfficialConformanceTargetIdentity
        {
            IdentitySha256 = new string('d', 64),
            Transport = "http",
            ProtocolVersion = "2026-07-28"
        },
        Scenarios = scenarios ?? []
    };

    private static AssuranceEvaluationContext CompleteProviderContext(
        ProviderHarnessStatus harnessStatus = ProviderHarnessStatus.Verified) => new()
    {
        ExecutionMode = ExecutionMode.Standard,
        HasSignedAttestation = true,
        ProviderHarness = new ProviderHarnessAssessment
        {
            Status = harnessStatus,
            AuthorizationVerified = harnessStatus == ProviderHarnessStatus.Verified,
            TargetIdentitySha256 = new string('a', 64),
            AcceptedRunCount = 3,
            RepeatedRunConsistency = 1
        }
    };

    private static AssuranceProfileManifest CertificateProfile(
        double minimumCoverage = 0,
        double minimumConfidence = 0,
        bool requiresAttestation = false,
        bool requiresRepeatedRuns = false,
        double minimumConsistency = 0) => new()
    {
        ProfileId = "test-certificate",
        Revision = "test",
        MinimumExecutionMode = ExecutionMode.Standard,
        MinimumEvidenceCoverageRatio = minimumCoverage,
        MinimumEvidenceConfidenceRatio = minimumConfidence,
        MinimumRepeatedRunConsistency = minimumConsistency,
        RequiresSignedAttestation = requiresAttestation,
        RequiresRepeatedRuns = requiresRepeatedRuns,
        CertificateEligible = true
    };
}