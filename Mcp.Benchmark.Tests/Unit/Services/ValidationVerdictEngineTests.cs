using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ValidationVerdictEngineTests
{
    [Fact]
    public void DetermineValidationStatus_WithTrustedCompleteEvidence_ReturnsPassed()
    {
        var status = ValidationVerdictEngine.DetermineValidationStatus(new VerdictAssessment
        {
            BaselineVerdict = ValidationVerdict.Trusted,
            ProtocolVerdict = ValidationVerdict.Trusted,
            CoverageVerdict = ValidationVerdict.Trusted
        });

        status.Should().Be(ValidationStatus.Passed);
    }

    [Fact]
    public void Calculate_ShouldAttachCurrentDecisionPolicyManifest()
    {
        var assessment = ValidationVerdictEngine.Calculate(new ValidationResult());

        assessment.RulesetVersion.Should().Be(DecisionPolicyManifest.CurrentVersion);
        assessment.Policy.Version.Should().Be(DecisionPolicyManifest.CurrentVersion);
        assessment.Policy.Thresholds.Should().ContainKey("score.pass");
        assessment.Policy.Weights.Should().ContainKey("trust.security");
    }

    [Fact]
    public void Calculate_PolicyManifest_ShouldContainEveryRuntimeDecisionRuleRevision()
    {
        var result = CreateResultWithFinding(
            GateOutcome.ReviewRequired,
            ValidationRuleSource.Heuristic,
            [ImpactArea.OperationalResilience]);

        var assessment = ValidationVerdictEngine.Calculate(result);

        var everyRuleIsVersioned = assessment.TriggeredDecisions.Concat(assessment.CoverageDecisions)
            .Where(decision => decision.RuleId != null)
            .All(decision =>
                assessment.Policy.RuleRevisions.TryGetValue(decision.RuleId!, out var revision) &&
                revision == decision.RuleRevision);
        everyRuleIsVersioned.Should().BeTrue();
    }

    [Fact]
    public void DetermineValidationStatus_WithDeterministicReject_ReturnsFailed()
    {
        var status = ValidationVerdictEngine.DetermineValidationStatus(new VerdictAssessment
        {
            BaselineVerdict = ValidationVerdict.Reject,
            ProtocolVerdict = ValidationVerdict.Trusted,
            CoverageVerdict = ValidationVerdict.Trusted
        });

        status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public void DetermineValidationStatus_WithCoverageReviewOnly_ReturnsPartiallyCompleted()
    {
        var status = ValidationVerdictEngine.DetermineValidationStatus(new VerdictAssessment
        {
            BaselineVerdict = ValidationVerdict.ConditionallyAcceptable,
            ProtocolVerdict = ValidationVerdict.ConditionallyAcceptable,
            CoverageVerdict = ValidationVerdict.ReviewRequired
        });

        status.Should().Be(ValidationStatus.PartiallyCompleted);
    }

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
        decision.RuleRevision.Should().Be(DecisionPolicyManifest.CurrentVersion);
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
            ObservedOutcome = ValidationOutcome.Succeeded,
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

    [Fact]
    public void Calculate_TypedBoundaryDecision_ShouldNotDependOnDisplayProse()
    {
        static DecisionRecord Calculate(string category, string description)
        {
            var result = new ValidationResult
            {
                TrustAssessment = new McpTrustAssessment
                {
                    BoundaryFindings =
                    [
                        new AiBoundaryFinding
                        {
                            Category = category,
                            Kind = AiBoundaryKind.DataExfiltration,
                            Component = "export",
                            Severity = "display-only",
                            SeverityLevel = ValidationFindingSeverity.High,
                            Gate = GateOutcome.Reject,
                            ImpactAreas = [ImpactArea.DataExposure],
                            Description = description
                        }
                    ]
                }
            };
            return ValidationVerdictEngine.Calculate(result).TriggeredDecisions.Single();
        }

        var first = Calculate("benign display", "ordinary words");
        var second = Calculate("exfiltration prompt injection", "critical token exploit words");

        second.Gate.Should().Be(first.Gate);
        second.Severity.Should().Be(first.Severity);
        second.ImpactAreas.Should().Equal(first.ImpactAreas);
    }

        [Fact]
        public void Calculate_CoveredFailedOutcomeWithoutFinding_ShouldReject()
        {
            var result = new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Failed }
            };
            result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = "protocol-core",
                Scope = "json-rpc",
                Status = ValidationCoverageStatus.Covered,
                ObservedOutcome = ValidationOutcome.Failed,
                Confidence = EvidenceConfidenceLevel.High
            });

            var assessment = ValidationVerdictEngine.Calculate(result);

            assessment.BaselineVerdict.Should().Be(ValidationVerdict.Reject);
            assessment.BlockingDecisions.Should().Contain(decision =>
                decision.RuleId == "MCP.OUTCOME.DETERMINISTIC_FAILURE" && decision.Gate == GateOutcome.Reject);
        }

        [Fact]
        public void Calculate_CoveredErrorAndBlockedDebt_ShouldRejectAndPublishManifest()
        {
            var result = new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Error }
            };
            result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = "protocol-core",
                Scope = "json-rpc",
                Status = ValidationCoverageStatus.Covered,
                ObservedOutcome = ValidationOutcome.Error,
                Confidence = EvidenceConfidenceLevel.High
            });
            result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = "tool-surface",
                Scope = "discovery",
                Status = ValidationCoverageStatus.Blocked,
                ObservedOutcome = ValidationOutcome.Blocked,
                Blocker = ValidationEvidenceBlocker.TransportError,
                Confidence = EvidenceConfidenceLevel.None
            });

            var assessment = ValidationVerdictEngine.Calculate(result);

            assessment.BaselineVerdict.Should().Be(ValidationVerdict.Reject);
            assessment.ProtocolVerdict.Should().Be(ValidationVerdict.Reject);
            assessment.CoverageVerdict.Should().Be(ValidationVerdict.ReviewRequired);
            assessment.TriggeredDecisions.Should().Contain(decision =>
                decision.RuleId == "MCP.OUTCOME.DETERMINISTIC_FAILURE" && decision.Gate == GateOutcome.Reject);
            assessment.CoverageDecisions.Should().Contain(decision => decision.Gate == GateOutcome.CoverageDebt);
            assessment.Policy.Parameters.Should().NotBeEmpty();
            assessment.Policy.PatternSets.Should().ContainKey("toolError.upstreamHttpStatusRegex");
        }

        [Fact]
        public void Calculate_PolicyManifestCollections_ShouldRejectMutableInterfaceWrites()
        {
            var policy = ValidationVerdictEngine.Calculate(new ValidationResult()).Policy;

            var parameters = (IDictionary<string, double>)policy.Parameters;
            var patterns = (IList<string>)policy.PatternSets["promptInjection"];

            var mutateParameter = () => parameters["new"] = 1;
            var mutatePattern = () => patterns.Add("new");

            mutateParameter.Should().Throw<NotSupportedException>();
            mutatePattern.Should().Throw<NotSupportedException>();
        }

        [Theory]
        [InlineData(GateOutcome.Note, ValidationVerdict.ConditionallyAcceptable)]
        [InlineData(GateOutcome.CoverageDebt, ValidationVerdict.ReviewRequired)]
        [InlineData(GateOutcome.ReviewRequired, ValidationVerdict.ReviewRequired)]
        [InlineData(GateOutcome.Reject, ValidationVerdict.Reject)]
        public void Calculate_SingleTypedGate_ShouldProduceExactBaselineVerdict(GateOutcome gate, ValidationVerdict expected)
        {
            var result = CreateResultWithFinding(gate, ValidationRuleSource.Heuristic, [ImpactArea.OperationalResilience]);

            ValidationVerdictEngine.Calculate(result).BaselineVerdict.Should().Be(expected);
        }

        [Fact]
        public void Calculate_MixedGates_ShouldUseMostRestrictiveVerdict()
        {
            var result = CreateResultWithFinding(GateOutcome.Note, ValidationRuleSource.Heuristic, [ImpactArea.OperationalResilience]);
            result.ToolValidation!.Findings.Add(CreateFinding("reject", GateOutcome.Reject, ValidationRuleSource.Heuristic, [ImpactArea.OperationalResilience]));
            result.ToolValidation.Findings.Add(CreateFinding("review", GateOutcome.ReviewRequired, ValidationRuleSource.Heuristic, [ImpactArea.OperationalResilience]));

            ValidationVerdictEngine.Calculate(result).BaselineVerdict.Should().Be(ValidationVerdict.Reject);
        }

        [Fact]
        public void Calculate_ProtocolVerdict_ShouldUseAuthorityOrProtocolImpactOnly()
        {
            var result = CreateResultWithFinding(GateOutcome.Note, ValidationRuleSource.Heuristic, [ImpactArea.OperationalResilience]);
            result.ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed };
            ValidationVerdictEngine.Calculate(result).ProtocolVerdict.Should().Be(ValidationVerdict.Trusted);

            result.ToolValidation!.Findings.Add(CreateFinding("protocol-note", GateOutcome.Note, ValidationRuleSource.Heuristic, [ImpactArea.ProtocolInteroperability]));
            ValidationVerdictEngine.Calculate(result).ProtocolVerdict.Should().Be(ValidationVerdict.ConditionallyAcceptable);

            result.ToolValidation.Findings.Add(CreateFinding("spec-review", GateOutcome.ReviewRequired, ValidationRuleSource.Spec, [ImpactArea.OperationalResilience]));
            ValidationVerdictEngine.Calculate(result).ProtocolVerdict.Should().Be(ValidationVerdict.ReviewRequired);

            result.ToolValidation.Findings.Add(CreateFinding("protocol-reject", GateOutcome.Reject, ValidationRuleSource.Heuristic, [ImpactArea.ProtocolInteroperability]));
            ValidationVerdictEngine.Calculate(result).ProtocolVerdict.Should().Be(ValidationVerdict.Reject);
        }

        [Fact]
        public void Calculate_ProtocolVerdict_ShouldDistinguishMissingAndAvailableProtocolResult()
        {
            ValidationVerdictEngine.Calculate(new ValidationResult()).ProtocolVerdict.Should().Be(ValidationVerdict.Unknown);
            ValidationVerdictEngine.Calculate(new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed }
            }).ProtocolVerdict.Should().Be(ValidationVerdict.Trusted);
            ValidationVerdictEngine.Calculate(new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Inconclusive }
            }).ProtocolVerdict.Should().Be(ValidationVerdict.Unknown);
            ValidationVerdictEngine.Calculate(new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Error }
            }).ProtocolVerdict.Should().Be(ValidationVerdict.Reject);
        }

        [Fact]
        public void Calculate_CoverageVerdict_ShouldDistinguishMissingCompleteAndDebt()
        {
            ValidationVerdictEngine.Calculate(new ValidationResult()).CoverageVerdict.Should().Be(ValidationVerdict.Unknown);

            var complete = new ValidationResult();
            complete.Evidence.Coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = "protocol",
                Scope = "complete",
                Status = ValidationCoverageStatus.Covered,
                ObservedOutcome = ValidationOutcome.Succeeded,
                Confidence = EvidenceConfidenceLevel.High
            });
            ValidationVerdictEngine.Calculate(complete).CoverageVerdict.Should().Be(ValidationVerdict.Trusted);

            var debt = new ValidationResult();
            debt.Evidence.Coverage.Add(new ValidationCoverageDeclaration
            {
                LayerId = "protocol",
                Scope = "blocked",
                Status = ValidationCoverageStatus.Blocked,
                ObservedOutcome = ValidationOutcome.Blocked,
                Blocker = ValidationEvidenceBlocker.TransportError
            });
            ValidationVerdictEngine.Calculate(debt).CoverageVerdict.Should().Be(ValidationVerdict.ReviewRequired);
        }

        private static ValidationResult CreateResultWithFinding(
            GateOutcome gate,
            ValidationRuleSource source,
            IReadOnlyCollection<ImpactArea> impacts) => new()
        {
            ToolValidation = new ToolTestResult
            {
                Findings = [CreateFinding("single", gate, source, impacts)]
            }
        };

        private static ValidationFinding CreateFinding(
            string suffix,
            GateOutcome gate,
            ValidationRuleSource source,
            IReadOnlyCollection<ImpactArea> impacts) => new()
        {
            RuleId = $"TEST.{suffix}",
            Category = "Test",
            Component = suffix,
            Source = source,
            Severity = gate == GateOutcome.Reject ? ValidationFindingSeverity.Critical : ValidationFindingSeverity.Low,
            GateOverride = gate,
            ImpactAreas = impacts.ToList(),
            Summary = $"Typed {suffix} finding."
        };
}
