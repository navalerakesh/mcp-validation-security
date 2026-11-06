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

    [Theory]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.Passed)]
    [InlineData(CoreConformanceStatus.Nonconformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.IssuesFound, OperationalReadinessStatus.Ready, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.NotReady, false, ValidationStatus.Failed)]
    [InlineData(CoreConformanceStatus.Incomplete, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Incomplete, OperationalReadinessStatus.Ready, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Incomplete, false, ValidationStatus.PartiallyCompleted)]
    [InlineData(CoreConformanceStatus.Conformant, SecurityAssuranceStatus.Verified, OperationalReadinessStatus.Ready, true, ValidationStatus.Error)]
    public void DetermineValidationStatus_ShouldRespectIndependentAssessmentPrecedence(
        CoreConformanceStatus core,
        SecurityAssuranceStatus security,
        OperationalReadinessStatus operations,
        bool hasCriticalErrors,
        ValidationStatus expected)
    {
        var evaluation = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            Assessments = new AssuranceAssessmentSet
            {
                CoreConformance = new CoreConformanceAssessment { Status = core },
                SecurityAssurance = new SecurityAssuranceAssessment { Status = security },
                OperationalReadiness = new OperationalReadinessAssessment { Status = operations }
            },
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = AssuranceProfileCatalog.Inventory.ProfileId,
                Status = CertificateEligibilityStatus.Ineligible
            }
        };

        AssuranceAssessmentEngine.DetermineValidationStatus(evaluation, hasCriticalErrors).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_HeuristicReject_ShouldRequireReviewWithoutCoreNonconformance()
    {
        var result = CreateResultWithCoveredBaseline();
        result.ToolValidation = new ToolTestResult();
        result.ToolValidation.AiReadinessFindings.Add(new ValidationFinding
        {
            RuleId = "MCP.CONTENT.SystemImpact",
            Source = ValidationRuleSource.Heuristic,
            GateOverride = GateOutcome.Reject,
            Severity = ValidationFindingSeverity.High,
            Category = "ContentSafety.SystemImpact",
            Component = "delete_file",
            Summary = "Name suggests system impact.",
            ImpactAreas = [ImpactArea.UnsafeAutonomy]
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Claims.Should().ContainSingle(claim =>
            claim.Metadata.GetValueOrDefault("ruleId") == "MCP.CONTENT.SystemImpact" &&
            claim.Authority == AssuranceAuthority.Heuristic &&
            claim.Outcome == AssuranceClaimOutcome.ReviewRequired);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Incomplete);
        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Ineligible);
    }

    [Fact]
    public void Evaluate_MustViolation_ShouldMakeCoreNonconformant()
    {
        var result = CreateResultWithCoveredBaseline();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Violations =
            [
                new ComplianceViolation
                {
                    CheckId = "MCP.JSONRPC.REQUEST_ID",
                    Category = "JSON-RPC",
                    Description = "Requests must include a non-null ID.",
                    Severity = ViolationSeverity.High
                }
            ]
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

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
        result.ProtocolCompliance!.Findings.Add(new ValidationFinding
        {
            RuleId = "MCP.SHOULD.DESCRIPTION",
            Source = ValidationRuleSource.Spec,
            Component = "optional",
            Severity = ValidationFindingSeverity.Medium,
            Summary = "Recommended behavior is absent.",
            Metadata = new Dictionary<string, string> { ["requirementLevel"] = "Should" }
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
        result.SecurityTesting!.AttackSimulations.Clear();
        result.SecurityTesting.AttackSimulations.Add(new AttackSimulationResult
        {
            AttackVector = "security-control",
            Description = "Security control outcome.",
            Authority = ValidationRuleSource.Guideline,
            Gate = gate,
            Outcome = gate == GateOutcome.Reject ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive,
            Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.High : ValidationFindingSeverity.Medium
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(expected);
        if (expected == SecurityAssuranceStatus.IssuesFound)
            evaluation.Assessments.SecurityAssurance.IssueClaimIds.Should().ContainSingle("attack:security-control");
        else
            evaluation.Assessments.SecurityAssurance.IncompleteClaimIds.Should().ContainSingle("attack:security-control");
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
        result.PerformanceTesting!.Findings.Add(new ValidationFinding
        {
            RuleId = "OPERATIONS.OBJECTIVE",
            Source = ValidationRuleSource.Spec,
            Component = "operations-objective",
            Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.High : ValidationFindingSeverity.Medium,
            GateOverride = gate,
            ImpactAreas = [ImpactArea.OperationalResilience],
            Summary = "Operational objective outcome."
        });

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
        evaluation.Assessments.OperationalReadiness.IncompleteClaimIds.Should().Contain("coverage:performance:repeated-measurements");
    }

    [Fact]
    public void Evaluate_HeuristicReview_ShouldNotDowngradeVerifiedDeterministicSecurity()
    {
        var result = CreateCompleteResult();
        result.ToolValidation = new ToolTestResult();
        result.ToolValidation.AiReadinessFindings.Add(new ValidationFinding
        {
            RuleId = "HEURISTIC.REVIEW",
            Source = ValidationRuleSource.Heuristic,
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
    public void Evaluate_OperationalRequirementOutsideOperationsDimension_ShouldNotAffectReadiness()
    {
        var result = CreateCompleteResult();
        result.SecurityTesting!.Findings.Add(new ValidationFinding
        {
            RuleId = "SECURITY.OPERATIONAL_LABEL",
            Source = ValidationRuleSource.Spec,
            Component = "security-control",
            Severity = ValidationFindingSeverity.High,
            ImpactAreas = [ImpactArea.AuthenticationBoundary],
            Summary = "Security failure carrying an operational requirement label.",
            Metadata = new Dictionary<string, string> { ["requirementLevel"] = "OperationalObjective" }
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.IssuesFound);
        evaluation.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
    }

    [Fact]
    public void Evaluate_ExtensionAndClientAssessments_ShouldBeOrdinallyStable()
    {
        var result = CreateCompleteResult();
        result.Assessments.Extensions.Add(CreateExtension("z.extension"));
        var extension = CreateExtension("a.extension",
        [
            new ValidationFinding
            {
                RuleId = "Z.RULE",
                Source = ValidationRuleSource.Heuristic,
                Component = "z-component",
                Summary = "Z review finding."
            },
            new ValidationFinding
            {
                RuleId = "A.RULE",
                Source = ValidationRuleSource.Heuristic,
                Component = "a-component",
                Summary = "A review finding."
            }
        ]);
        result.Assessments.Extensions.Add(extension);
        result.ClientCompatibility = new ClientCompatibilityReport
        {
            Assessments =
            [
                new ClientProfileAssessment { ProfileId = "z-client", Revision = "1", Summary = "Z" },
                new ClientProfileAssessment { ProfileId = "a-client", Revision = "1", Summary = "A" }
            ]
        };

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.ExtensionReadiness.Select(item => item.ExtensionId).Should().Equal("a.extension", "z.extension");
        evaluation.Assessments.ExtensionReadiness[0].NonSuccessClaimIds.Should().BeInAscendingOrder(StringComparer.Ordinal);
        evaluation.Assessments.ClientCompatibility.Select(item => item.ProfileId).Should().Equal("a-client", "z-client");
        evaluation.Assessments.ClientCompatibility.Should().OnlyContain(item => item.Verification == ClientCompatibilityVerification.Interpreted);
    }

    [Theory]
    [InlineData(0, ExternalConformanceStatus.Verified)]
    [InlineData(1, ExternalConformanceStatus.Nonconformant)]
    public void Evaluate_VerifiedExternalConformance_ShouldHonorFailureBoundary(
        int failed,
        ExternalConformanceStatus expected)
    {
        var result = CreateCompleteResult();
        result.Assessments.OfficialConformance = CreateOfficialConformance(
            OfficialConformanceVerificationStatus.Verified,
            failed: failed);

        var evaluation = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        evaluation.Assessments.ExternalConformance.Status.Should().Be(expected);
        evaluation.Assessments.ExternalConformance.Failed.Should().Be(failed);
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
        if (verificationStatus == OfficialConformanceVerificationStatus.Conflict)
        {
            evaluation.CertificateEligibility.Reasons.Should().Contain(new[]
            {
                "Verified official conformance evidence is required.",
                "Official and native evidence conflicts remain unresolved."
            });
        }
        else
        {
            evaluation.CertificateEligibility.Reasons.Should().Contain("Verified official conformance evidence is required.");
        }
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
    public void Evaluate_MissingRequiredExtension_ShouldRequireReview()
    {
        var result = CreateCompleteResult();
        var evaluation = AssuranceAssessmentEngine.Evaluate(
            result,
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                RequiredExtensionIds = ["io.modelcontextprotocol/ui"]
            });

        evaluation.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.ReviewRequired);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle()
            .Which.Should().Be("Required extension 'io.modelcontextprotocol/ui' was not assessed.");
    }

    [Theory]
    [InlineData(ValidationVerdict.Reject, CertificateEligibilityStatus.Ineligible)]
    [InlineData(ValidationVerdict.ReviewRequired, CertificateEligibilityStatus.ReviewRequired)]
    [InlineData(ValidationVerdict.Trusted, CertificateEligibilityStatus.Eligible)]
    public void Evaluate_RequiredExtensionOutcome_ShouldAffectCertificate(
        ValidationVerdict extensionVerdict,
        CertificateEligibilityStatus expected)
    {
        var result = CreateCompleteResult();
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "io.modelcontextprotocol/ui",
            Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "2026-01-26",
            SpecificationSha256 = new string('e', 64),
            Verdict = extensionVerdict,
            Findings = extensionVerdict switch
            {
                ValidationVerdict.Reject =>
                [
                    new ValidationFinding
                    {
                        RuleId = "EXT.REQUIRED",
                        Source = ValidationRuleSource.Spec,
                        Component = "extension",
                        Severity = ValidationFindingSeverity.High,
                        Summary = "Required extension behavior failed."
                    }
                ],
                ValidationVerdict.ReviewRequired =>
                [
                    new ValidationFinding
                    {
                        RuleId = "EXT.REVIEW",
                        Source = ValidationRuleSource.Heuristic,
                        Component = "extension",
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = "Extension behavior requires review."
                    }
                ],
                _ => []
            }
        });

        var evaluation = AssuranceAssessmentEngine.Evaluate(
            result,
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                RequiredExtensionIds = ["io.modelcontextprotocol/ui"]
            });

        evaluation.CertificateEligibility.Status.Should().Be(expected);
        if (extensionVerdict == ValidationVerdict.Trusted)
        {
            evaluation.CertificateEligibility.Reasons.Should().BeEmpty();
            return;
        }

        evaluation.CertificateEligibility.Reasons.Should().ContainSingle().Which.Should().Be(extensionVerdict switch
        {
            ValidationVerdict.Reject => "Required extension 'io.modelcontextprotocol/ui' was rejected.",
            ValidationVerdict.ReviewRequired => "Required extension 'io.modelcontextprotocol/ui' is Incomplete.",
            _ => throw new InvalidOperationException("Unexpected extension verdict.")
        }, "non-ready extension outcomes must retain stable machine-readable reasons");
    }

    [Theory]
    [InlineData(1, 0, CertificateEligibilityStatus.ReviewRequired)]
    [InlineData(0, 1, CertificateEligibilityStatus.Ineligible)]
    public void Evaluate_Waivers_ShouldPreventProviderEligibility(
        int activeWaivers,
        int invalidWaivers,
        CertificateEligibilityStatus expected)
    {
        var evaluation = AssuranceAssessmentEngine.Evaluate(
            CreateCompleteResult(),
            CertificateProfile(),
            new AssuranceEvaluationContext
            {
                ExecutionMode = ExecutionMode.Standard,
                ActiveWaiverCount = activeWaivers,
                InvalidWaiverCount = invalidWaivers
            });

        evaluation.CertificateEligibility.Status.Should().Be(expected);
        evaluation.CertificateEligibility.Reasons.Should().ContainSingle().Which.Should().Be(
            activeWaivers > 0
                ? $"Provider certification does not permit {activeWaivers} applied waiver(s)."
                : $"Provider certification found {invalidWaivers} expired or invalid waiver(s).");
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
        evaluation.CertificateEligibility.Reasons.Should().Contain("Execution mode Safe is below required mode Standard.");
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
            ProtocolCompliance = new ComplianceTestResult(),
            SecurityTesting = new SecurityTestResult
            {
                AttackSimulations =
                [
                    new AttackSimulationResult
                    {
                        AttackVector = "security-control",
                        Description = "Deterministic security control passed.",
                        Authority = ValidationRuleSource.Guideline,
                        Outcome = ValidationOutcome.Succeeded,
                        Gate = GateOutcome.Note,
                        Severity = ValidationFindingSeverity.Info,
                        DefenseSuccessful = true
                    }
                ]
            },
            PerformanceTesting = new PerformanceTestResult()
        };
        result.Assessments.OfficialConformance = CreateOfficialConformance(OfficialConformanceVerificationStatus.Verified);
        AddCovered(result, "protocol-core", "json-rpc");
        AddCovered(result, "security-controls", "authentication");
        AddCovered(result, "performance", "repeated-measurements");
        return result;
    }

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

    private static ExtensionAssessment CreateExtension(
        string extensionId,
        IReadOnlyList<ValidationFinding>? findings = null) => new()
    {
        ExtensionId = extensionId,
        Stage = ExtensionLifecycleStage.Stable,
        SpecificationRevision = "1",
        SpecificationSha256 = new string('e', 64),
        Verdict = ValidationVerdict.Trusted,
        Findings = findings ?? [],
        EvidenceSummary = new EvidenceCoverageSummary
        {
            TotalDeclarations = 1,
            ApplicableDeclarations = 1,
            Covered = 1,
            ConfidenceLevel = EvidenceConfidenceLevel.High
        }
    };

    private static OfficialConformanceAssessment CreateOfficialConformance(
        OfficialConformanceVerificationStatus status,
        IReadOnlyList<OfficialConformanceScenarioAssessment>? scenarios = null,
        int failed = 0) => new()
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
        Failed = failed,
        Scenarios = scenarios ??
        [
            new OfficialConformanceScenarioAssessment
            {
                ScenarioId = "required-core",
                ScoringDisposition = OfficialConformanceScoringDisposition.Required,
                Checks =
                [
                    new OfficialConformanceCheckAssessment
                    {
                        Id = "required-success",
                        Name = "required-success",
                        Description = "Required official check passed.",
                        Status = OfficialConformanceCheckStatus.Success
                    }
                ]
            }
        ]
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