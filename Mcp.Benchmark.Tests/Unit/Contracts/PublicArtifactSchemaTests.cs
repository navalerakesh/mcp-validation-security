using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Contracts;

public sealed class PublicArtifactSchemaTests
{
    [Fact]
    public void OfficialCheckProvenance_ShouldRejectInvalidIndicesAndContradictoryApplicability()
    {
        var document = Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateDocument(
            [Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateCheck("schema-check", OfficialConformanceCheckStatus.Success)]);
        var imported = new Mcp.Benchmark.Infrastructure.Services.OfficialConformanceEvidenceImporter()
            .Import(JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions()));
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = imported.Assessment;
        var serialized = Serialize(result);
        var check = serialized["measurements"]!["officialConformance"]!["scenarios"]![0]!["checks"]![0]!;
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        check["sourceIndex"] = -1;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        check["sourceIndex"] = OfficialConformanceContract.MaximumChecksPerScenario;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        check["sourceIndex"] = OfficialConformanceContract.MaximumChecksPerScenario - 1;
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        check["applicability"] = "unknown";
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        check["applicability"] = "notApplicable";
        check["status"] = "failure";
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        check["status"] = "info";
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        check["prerequisiteMissing"] = true;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        check["status"] = "failure";
        check["applicability"] = "applicable";
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        check["status"] = "success";
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
    }

    [Fact]
    public void SelectedSecurityBaseline_ShouldValidateAndReverifyAfterRoundTrip()
    {
        var result = new ValidationResult();
        Mcp.Benchmark.Tests.Unit.Services.ControlledIdentityEvaluatorTests.AddSecurityBaseline(result);
        var serialized = Serialize(result.CloneWithoutSecrets());
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var restored = JsonSerializer.Deserialize<ValidationResult>(serialized.ToJsonString(), options)!;
        var replay = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(restored, AssuranceProfileCatalog.Comprehensive);
        replay.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
            .Should().BeEquivalentTo(result.Assurance.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal)));
        replay.RequiredIdentityControls.Should().NotBeNull();
        var selectedContract = serialized["assurance"]!["requiredIdentityControls"]!["contract"]!;
        selectedContract["contractRevision"] = ControlledIdentityMatrixContract.LegacyRevision;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        selectedContract["contractRevision"] = ControlledIdentityMatrixContract.CurrentRevision;
        serialized["assurance"]!["requiredIdentityControls"]!.AsObject().Remove("scopeEnforcementCaseId");
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
    }

    [Theory]
    [InlineData("timingSampleCount")]
    [InlineData("peakInFlightRequests")]
    [InlineData("observationDurationMs")]
    public void LoadEvidenceProvenance_ShouldRejectInvalidValuesAndAllowHistoricalOmission(string property)
    {
        var result = new ValidationResult { PerformanceTesting = new PerformanceTestResult
        {
            LoadTesting = new LoadTestResult { TimingSampleCount = 2, PeakInFlightRequests = 2, ObservationDurationMs = 1.5 }
        }};
        var serialized = Serialize(result.CloneWithoutSecrets());
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        var load = serialized["measurements"]!["performanceTesting"]!["loadTesting"]!.AsObject();
        load[property] = -1;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        load[property] = "unknown";
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        load.Remove(property);
        AssertConforms("mcpval-validation-result.schema.json", serialized);
    }

    private readonly JsonSchemaValidator _validator = new();

    [Fact]
    public void RequestFailureBreakdown_ShouldRoundTripAndRejectInvalidAttribution()
    {
        var result = new ValidationResult();
        using var run = Mcp.Benchmark.Core.Services.ValidationObservability.BeginRun(result.ValidationId, 1);
        Mcp.Benchmark.Core.Services.ValidationObservability.RecordRequestStarted("POST");
        Mcp.Benchmark.Core.Services.ValidationObservability.RecordRequestCompleted("POST", 1, false, 405);
        result.Run.OperationalMetrics = run.Complete(10);
        var serialized = Serialize(result.CloneWithoutSecrets());
        AssertConforms("mcpval-validation-result.schema.json", serialized);
        var failure = serialized["run"]!["operationalMetrics"]!["requestFailures"]![0]!;
        failure["httpStatusCode"] = null;
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        failure["httpStatusCode"] = 405;
        failure["method"] = "secret-canary";
        AssertDoesNotConform("mcpval-validation-result.schema.json", serialized);
        serialized["run"]!["operationalMetrics"]!.AsObject().Remove("requestFailures");
        serialized["documentSchemaVersion"] = "3.2.0";
        AssertConforms("mcpval-validation-result.schema.json", serialized);
    }

    [Fact]
    public void ValidationResult_PublicJson_ConformsToPublishedSchema()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.PartiallyCompleted,
            ComplianceScore = 85,
            BaselineComparison = new AssuranceBaselineComparison
            {
                BaselineValidationId = "baseline-validation",
                BaselineDocumentSchemaVersion = ArtifactContracts.ValidationResultSchemaVersion,
                ProfileId = "provider",
                ProfileRevision = AssuranceProfileCatalog.Revision,
                CoreConformanceBefore = CoreConformanceStatus.Conformant,
                CoreConformanceAfter = CoreConformanceStatus.Nonconformant,
                SecurityAssuranceBefore = SecurityAssuranceStatus.Verified,
                SecurityAssuranceAfter = SecurityAssuranceStatus.IssuesFound,
                OperationalReadinessBefore = OperationalReadinessStatus.Ready,
                OperationalReadinessAfter = OperationalReadinessStatus.Incomplete,
                CertificateEligibilityBefore = CertificateEligibilityStatus.Eligible,
                CertificateEligibilityAfter = CertificateEligibilityStatus.Ineligible,
                EvidenceCoverageDelta = -0.1,
                EvidenceConfidenceDelta = -0.05,
                IsRegression = true,
                NewNonSuccessClaims =
                [
                    new BaselineClaimChange(
                        "security:new-issue",
                        AssuranceDimension.Security,
                        AssuranceAuthority.DeterministicSecurity,
                        AssuranceClaimOutcome.Violated,
                        "target")
                ]
            }
        };
        result.Run.OperationalMetrics = new ValidationOperationalMetrics
        {
            RunCorrelationId = result.ValidationId,
            ValidatorOverheadMs = 10,
            RequestBudgetLimit = 20,
            RequestsStarted = 2,
            RequestsCompleted = 2,
            TargetLatencyP50Ms = 5,
            TargetLatencyP95Ms = 8,
            TargetLatencyP99Ms = 9,
            StageDurationMs = new Dictionary<string, double> { ["validation.verdict"] = 1 }
        };

        AssertConforms("mcpval-validation-result.schema.json", Serialize(result));

        result.Assurance = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);
        var comprehensive = Serialize(result);
        comprehensive["baselineComparison"]!["profileId"] = "comprehensive";
        AssertConforms("mcpval-validation-result.schema.json", comprehensive);
        comprehensive["assurance"]!["profile"]!["profileId"] = "unknown-profile";
        AssertDoesNotConform("mcpval-validation-result.schema.json", comprehensive);
    }

    [Fact]
    public void AuditManifest_PublicJson_ConformsToPublishedSchema()
    {
        var manifest = new AuditManifest
        {
            ConfigurationSources = Enum.GetValues<ConfigurationSourceKind>()
                .Select(source => new ConfigurationSettingOrigin("$.server.timeoutMs", source,
                    "budget", null, "Source vocabulary contract fixture.")).ToArray(),
            CommandName = "validate",
            ValidationId = "validation-test",
            SessionId = "session-test",
            Target = "https://example.test/mcp",
            Transport = "http",
            RequestedProtocolProfile = "latest",
            ResolvedSchemaVersion = "2026-07-28",
            ProtocolEra = McpProtocolEra.Modern,
            ExecutionMode = ExecutionMode.Safe,
            PlannedChecks = ["protocol-compliance"],
            ExecutedChecks = ["protocol-compliance"],
            ArtifactPaths = [],
            Digests = new AuditDigestSet
            {
                ValidatorSha256 = new string('0', 64),
                RuleCatalogSha256 = new string('0', 64),
                ProfileCatalogSha256 = new string('0', 64),
                ConfigSha256 = new string('0', 64),
                TargetIdentitySha256 = new string('0', 64)
            }
        };

        var serialized = Serialize(manifest);
        AssertConforms("mcpval-audit-manifest.schema.json", serialized);
        serialized["assuranceProfile"] = "comprehensive";
        AssertConforms("mcpval-audit-manifest.schema.json", serialized);
        serialized["assuranceProfile"] = "inventory";
        serialized["documentSchemaVersion"] = "2.0.0";
        AssertConforms("mcpval-audit-manifest.schema.json", serialized);
        ArtifactContracts.IsCompatible(ArtifactContracts.AuditManifestDocumentType, "2.0.0").Should().BeTrue();
        ArtifactContracts.IsCompatible(ArtifactContracts.AuditManifestDocumentType, "2.1.0").Should().BeTrue();
        serialized["configurationSources"]![0]!["source"] = "unknown-source";
        AssertDoesNotConform("mcpval-audit-manifest.schema.json", serialized);
        serialized["configurationSources"]![0]!["source"] = "default";
        serialized["configurationSources"]![0]!["value"] = "canary-value";
        AssertDoesNotConform("mcpval-audit-manifest.schema.json", serialized);
    }

    [Fact]
    public void ValidationResult_CatalogEvidence_ShouldValidateTypedCountsAndCompletion()
    {
        var result = new ValidationResult
        {
            ToolValidation = new ToolTestResult
            {
                CatalogEnumeration = new CatalogEnumerationEvidence
                {
                    RawItemsObserved = 2, RecoveredItems = 1, RejectedItems = 1,
                    PageAttempts = 1, ParsedPages = 1, Termination = CatalogEnumerationTermination.Complete
                },
                AiReadinessAnalyzedSchemaCount = 0,
                AiReadinessInspectionIssues = [new SchemaInspectionIssue("fixture", SchemaInspectionStatus.UnresolvedReference)]
            }
        };
        var instance = Serialize(result);
        AssertConforms("mcpval-validation-result.schema.json", instance);
        instance["documentSchemaVersion"] = "2.0.0";
        AssertConforms("mcpval-validation-result.schema.json", instance);
        instance["measurements"]!["toolValidation"]!["catalogEnumeration"]!["isComplete"] = true;
        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
        instance["measurements"]!["toolValidation"]!["catalogEnumeration"]!["isComplete"] = false;
        instance["measurements"]!["toolValidation"]!["aiReadinessInspectionIssues"]![0]!["status"] = "unknown-status";
        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
    }

    [Fact]
    public void ValidationResult_OptionalCapabilityEvidence_ShouldRejectUnknownAndUnsupportedClaims()
    {
        foreach (var classification in Enum.GetValues<ProbeResponseClassification>())
        {
            var result = new ValidationResult
            {
                ProtocolCompliance = new ComplianceTestResult
                {
                    OptionalCapabilityObservations =
                    [
                        new CapabilityMethodObservation("logging", "logging/setLevel", true,
                            classification != ProbeResponseClassification.Unknown,
                            classification == ProbeResponseClassification.Success ? true :
                                classification == ProbeResponseClassification.ProtocolError ? false : null,
                            classification, null,
                            classification == ProbeResponseClassification.PolicyBlocked ? ValidationConstants.Methods.PromptsList : null)
                    ]
                }
            };
            var instance = Serialize(result);
            AssertConforms("mcpval-validation-result.schema.json", instance);
            foreach (var version in new[] { "2.0.0", "2.1.0", "2.2.0" })
            {
                instance["documentSchemaVersion"] = version;
                AssertConforms("mcpval-validation-result.schema.json", instance);
                ArtifactContracts.IsCompatible(ArtifactContracts.ValidationResultDocumentType, version).Should().BeTrue();
            }
            var observation = instance["measurements"]!["protocolCompliance"]!["optionalCapabilityObservations"]![0]!;
            observation["classification"] = "unknown-classification";
            AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
            observation["classification"] = "policyBlocked";
            observation["supported"] = true;
            AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
            observation["classification"] = "success";
            observation["attempted"] = false;
            observation["supported"] = null;
            AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
            observation["attempted"] = true;
            observation["supported"] = true;
            observation["statusCode"] = 401;
            AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
        }
    }

    [Fact]
    public void ValidationResult_RecordedClockAndExtensionObligations_ShouldValidateExplicitEvidence()
    {
        var result = new ValidationResult();
        result.Assessments.Extensions.Add(new ExtensionAssessment
        {
            ExtensionId = "fixture.extension", Stage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64),
            Verdict = ValidationVerdict.ReviewRequired,
            Coverage = [new ValidationCoverageDeclaration
            {
                LayerId = "fixture", Scope = "unstarted", Status = ValidationCoverageStatus.Blocked,
                ObservedOutcome = ValidationOutcome.Cancelled, Confidence = EvidenceConfidenceLevel.None
            }]
        });
        var instance = Serialize(result);
        AssertConforms("mcpval-validation-result.schema.json", instance);
        var recordedStart = instance["startTime"]!.DeepClone();
        instance.AsObject().Remove("startTime");
        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
        instance["startTime"] = "2020-01-01T12:00:00";
        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
        instance["startTime"] = recordedStart;
        instance["measurements"]!["extensions"]![0]!["coverage"]![0]!["status"] = "invented-status";
        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
    }

    [Fact]
    public void ProviderHarness_PublicJson_ConformsToPublishedSchema()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var manifest = new ProviderHarnessManifest
        {
            ProviderId = "provider-test",
            TargetIdentitySha256 = new string('a', 64),
            ProtocolRevision = "2026-07-28",
            Authorization = new ProviderAuthorizationGrant
            {
                GrantId = "grant-1",
                Authority = "provider-security-owner",
                TargetIdentitySha256 = new string('a', 64),
                ValidFromUtc = now.AddHours(-1),
                ExpiresAtUtc = now.AddHours(1),
                AllowedExecutionModes = [ExecutionMode.Standard],
                AllowedProbeClasses = ["protocol-active"],
                GrantDocumentSha256 = new string('b', 64),
                SigningKeyId = new string('c', 64),
                SignatureBase64 = Convert.ToBase64String(new byte[64])
            },
            SigningKeyId = new string('c', 64),
            ManifestDocumentSha256 = new string('d', 64),
            SignatureBase64 = Convert.ToBase64String(new byte[64]),
            Runs =
            [
                new ProviderHarnessRunReceipt
                {
                    RunId = "run-1",
                    ResultSha256 = new string('c', 64),
                    ExecutionPlanSha256 = new string('d', 64),
                    StartedAtUtc = now.AddMinutes(-2),
                    EndedAtUtc = now.AddMinutes(-1),
                    CoreConformance = CoreConformanceStatus.Conformant,
                    SecurityAssurance = SecurityAssuranceStatus.Verified,
                    OperationalReadiness = OperationalReadinessStatus.Ready
                }
            ]
        };

        AssertConforms("mcpval-provider-harness.schema.json", Serialize(manifest));
    }

    [Fact]
    public void ClientProfileSummary_PublicShape_ConformsToPublishedSchema()
    {
        var summary = new JsonObject
        {
            ["documentType"] = ArtifactContracts.ClientProfileSummaryDocumentType,
            ["documentSchemaVersion"] = ArtifactContracts.ClientProfileSummarySchemaVersion,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["validationId"] = "validation-test",
            ["validatorVersion"] = "1.1.25",
            ["serverEndpoint"] = "https://example.test/mcp",
            ["assuranceProfile"] = "provider",
            ["certificateEligibility"] = "ineligible",
            ["coreConformance"] = "incomplete",
            ["securityAssurance"] = "incomplete",
            ["operationalReadiness"] = "incomplete",
            ["policyPassed"] = false,
            ["policySummary"] = "Review required.",
            ["evidenceSummary"] = new JsonObject(),
            ["profileCount"] = 1,
            ["compatibleCount"] = 1,
            ["warningCount"] = 0,
            ["incompatibleCount"] = 0,
            ["profiles"] = new JsonArray(new JsonObject
            {
                ["profileId"] = "claude-code",
                ["displayName"] = "Claude Code",
                ["status"] = "Compatible",
                ["passedRequirements"] = 1,
                ["warningRequirements"] = 0,
                ["failedRequirements"] = 0,
                ["topBlockers"] = new JsonArray()
            })
        };

        AssertConforms("mcpval-client-profile-summary.schema.json", summary);
        summary["assuranceProfile"] = "comprehensive";
        AssertConforms("mcpval-client-profile-summary.schema.json", summary);
        summary["assuranceProfile"] = "unknown-profile";
        AssertDoesNotConform("mcpval-client-profile-summary.schema.json", summary);
    }

    [Fact]
    public void ModelEvaluation_PublicJson_ConformsToPublishedSchema()
    {
        var artifact = new ModelEvaluationArtifact
        {
            ValidationId = "validation-test",
            SessionId = "session-test",
            Provider = "builtin-rubric",
            Model = "builtin-rubric-v1",
            PromptSet = "builtin-default",
            Status = ModelEvaluationArtifactStatus.Completed,
            Summary = "Evaluation completed.",
            AssuranceProfile = "provider",
            CoreConformance = CoreConformanceStatus.Conformant,
            SecurityAssurance = SecurityAssuranceStatus.Verified,
            OperationalReadiness = OperationalReadinessStatus.Ready,
            CertificateEligibility = CertificateEligibilityStatus.Eligible
        };

        AssertConforms("mcpval-model-evaluation.schema.json", Serialize(artifact));
        var comprehensive = Serialize(artifact);
        comprehensive["assuranceProfile"] = "comprehensive";
        comprehensive["certificateEligibility"] = "ineligible";
        AssertConforms("mcpval-model-evaluation.schema.json", comprehensive);
        comprehensive["assuranceProfile"] = "unknown-profile";
        AssertDoesNotConform("mcpval-model-evaluation.schema.json", comprehensive);
    }

    [Fact]
    public void ValidationAttestation_PublicJson_ConformsToPublishedSchema()
    {
        var attestation = new ValidationAttestation
        {
            ValidationId = "validation-test",
            SubjectFile = "validation-result.json",
            SubjectSha256 = new string('0', 64),
            KeyId = new string('1', 64),
            PublicKeyPem = "-----BEGIN PUBLIC KEY-----\ntest\n-----END PUBLIC KEY-----",
            SignatureBase64 = "dGVzdA=="
        };

        AssertConforms("mcpval-validation-attestation.schema.json", Serialize(attestation));
    }

    [Fact]
    public void OfficialConformanceEvidence_PublicJson_ConformsToPublishedSchema()
    {
        var checks = new[]
        {
            new OfficialConformanceCheckRecord
            {
                Id = "initialize-version",
                Name = "Initialize version",
                Description = "Negotiated version is valid.",
                Status = OfficialConformanceCheckStatus.Success,
                Timestamp = DateTimeOffset.Parse("2026-11-04T10:00:00Z")
            }
        };
        var checksJson = OfficialConformanceContract.SerializeChecks(checks);
        var evidence = new OfficialConformanceEvidenceDocument
        {
            Runner = new OfficialConformanceRunnerIdentity
            {
                PackageName = OfficialConformanceContract.PackageName,
                Version = OfficialConformanceContract.PackageVersion,
                SourceCommit = OfficialConformanceContract.SourceCommit,
                PackageSha256 = OfficialConformanceContract.PackageSha256,
                PackageIntegrity = OfficialConformanceContract.PackageIntegrity
            },
            RequirementSet = new OfficialConformanceRequirementSet
            {
                Revision = OfficialConformanceContract.RequirementSetRevision,
                Sha256 = OfficialConformanceContract.RequirementSetSha256
            },
            Target = new OfficialConformanceTargetIdentity
            {
                IdentitySha256 = new string('0', 64),
                Transport = "http",
                ProtocolVersion = "2026-07-28"
            },
            Invocation = new OfficialConformanceInvocation { Mode = "server" },
            Run = new OfficialConformanceRunRecord
            {
                StartedAtUtc = DateTimeOffset.Parse("2026-11-04T10:00:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-11-04T10:00:01Z"),
                ExitCode = 0,
                State = OfficialConformanceRunState.Completed
            },
            Scenarios =
            [
                new OfficialConformanceScenarioRecord
                {
                    ScenarioId = "server-initialize",
                    ChecksSha256 = OfficialConformanceContract.ComputeChecksSha256(checksJson),
                    ChecksBase64 = Convert.ToBase64String(checksJson)
                }
            ]
        };

        AssertConforms(
            "mcpval-official-conformance-evidence.schema.json",
            JsonNode.Parse(JsonSerializer.Serialize(evidence, OfficialConformanceContract.CreateJsonOptions()))!);
    }

    [Fact]
    public void ValidationResult_MissingProducerIdentity_DoesNotConformToPublishedSchema()
    {
        var instance = Serialize(new ValidationResult());
        instance["producer"]!.AsObject().Remove("name");

        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
    }

    [Fact]
    public void ValidationResult_MalformedModernDiscovery_DoesNotConformToPublishedSchema()
    {
        var instance = Serialize(new ValidationResult());
        instance["run"]!["modernDiscovery"] = new JsonObject
        {
            ["isSuccessful"] = true,
            ["error"] = null,
            ["payload"] = new JsonObject { ["isValid"] = true },
            ["transport"] = new JsonObject()
        };

        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
    }

    [Fact]
    public void ValidationResult_MalformedPolicyAndAssuranceShapes_DoNotConformToPublishedSchema()
    {
        var result = new ValidationResult
        {
            PolicyOutcome = new ValidationPolicyOutcome
            {
                Mode = ValidationPolicyModes.Strict,
                Passed = false,
                Summary = "Blocked"
            }
        };
        var instance = Serialize(result);
        instance["policyDecision"]!["passed"] = "false";
        instance["assurance"]!["certificateEligibility"]!["evidenceCoverageRatio"] = 2;

        AssertDoesNotConform("mcpval-validation-result.schema.json", instance);
    }

    [Fact]
    public void AuditManifest_InvalidEvidenceRatio_DoesNotConformToPublishedSchema()
    {
        var manifest = new AuditManifest
        {
            CommandName = "validate",
            ValidationId = "validation-test",
            SessionId = "session-test",
            Target = "https://example.test/mcp",
            Transport = "http",
            RequestedProtocolProfile = "latest",
            ResolvedSchemaVersion = "2026-07-28",
            ProtocolEra = McpProtocolEra.Modern,
            ExecutionMode = ExecutionMode.Safe,
            Digests = CreateDigestSet(),
            EvidenceCompleteness = new EvidenceCoverageSummary { EvidenceCoverageRatio = 2 }
        };

        AssertDoesNotConform("mcpval-audit-manifest.schema.json", Serialize(manifest));
    }

    [Fact]
    public void ClientProfileSummary_IncompleteProfile_DoesNotConformToPublishedSchema()
    {
        var summary = new JsonObject
        {
            ["documentType"] = ArtifactContracts.ClientProfileSummaryDocumentType,
            ["documentSchemaVersion"] = ArtifactContracts.ClientProfileSummarySchemaVersion,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["validationId"] = "validation-test",
            ["profileCount"] = 1,
            ["compatibleCount"] = 1,
            ["warningCount"] = 0,
            ["incompatibleCount"] = 0,
            ["profiles"] = new JsonArray(new JsonObject { ["profileId"] = "profile-only" })
        };

        AssertDoesNotConform("mcpval-client-profile-summary.schema.json", summary);
    }

    [Theory]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "2.0.0", true)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "2.99.0", true)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "3.0.0", true)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "3.99.0", true)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "4.0.0", false)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "1.99.0", false)]
    [InlineData(ArtifactContracts.ModelEvaluationDocumentType, "1.0.0", false)]
    [InlineData(ArtifactContracts.ModelEvaluationDocumentType, "2.0.0", true)]
    [InlineData("mcpval.unknown", "1.0.0", false)]
    [InlineData(ArtifactContracts.AuditManifestDocumentType, "invalid", false)]
    [InlineData(ArtifactContracts.AuditManifestDocumentType, "1.-1.0", false)]
    [InlineData(ArtifactContracts.AuditManifestDocumentType, "1.01.0", false)]
    [InlineData(ArtifactContracts.AuditManifestDocumentType, "1.0.0 ", false)]
    public void ArtifactContracts_ShouldEnforceMajorVersionCompatibility(
        string documentType,
        string schemaVersion,
        bool expected)
    {
        ArtifactContracts.IsCompatible(documentType, schemaVersion).Should().Be(expected);
    }

    [Fact]
    public void ValidationResult_V1Fixture_ShouldBeRejectedByV2Contract()
    {
        var fixturePath = Path.Combine(GetRepositoryRoot(), "Mcp.Benchmark.Tests", "Fixtures", "Contracts", "validation-result-v1.0.json");
        var json = File.ReadAllText(fixturePath);

        var result = JsonSerializer.Deserialize<ValidationResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
        });

        result.Should().NotBeNull();
        result!.ValidationId.Should().Be("backward-fixture-v1");
        ArtifactContracts.IsCompatible(result.DocumentType, result.DocumentSchemaVersion).Should().BeFalse();
        AssertDoesNotConform("mcpval-validation-result.schema.json", JsonNode.Parse(json)!);
    }

    [Fact]
    public void ConfigurationSchema_ShouldMatchParserMetadataAndAcceptDefaultConfiguration()
    {
        const string schemaName = "mcpval-configuration-v1.schema.json";
        var schemaPath = Path.Combine(GetRepositoryRoot(), "docs", "Schemas", schemaName);
        var published = JsonNode.Parse(File.ReadAllText(schemaPath));
        var generated = Mcp.Benchmark.CLI.Utilities.BoundedArtifactReader.CreateConfigurationSchema();

        JsonNode.DeepEquals(published, generated).Should().BeTrue();
        AssertConforms(schemaName, JsonSerializer.SerializeToNode(new McpValidatorConfiguration())!);
    }

    private void AssertConforms(string schemaName, JsonNode instance)
    {
        var schemaPath = Path.Combine(GetRepositoryRoot(), "docs", "Schemas", schemaName);
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath));

        schema.Should().NotBeNull();
        var result = _validator.Validate(instance, schema!);
        result.IsValid.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
    }

    private void AssertDoesNotConform(string schemaName, JsonNode instance)
    {
        var schemaPath = Path.Combine(GetRepositoryRoot(), "docs", "Schemas", schemaName);
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath));

        schema.Should().NotBeNull();
        _validator.Validate(instance, schema!).IsValid.Should().BeFalse();
    }

    private static JsonNode Serialize<T>(T value)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));

        return JsonSerializer.SerializeToNode(value, options)!;
    }

    private static AuditDigestSet CreateDigestSet() => new()
    {
        ValidatorSha256 = new string('0', 64),
        RuleCatalogSha256 = new string('0', 64),
        ProfileCatalogSha256 = new string('0', 64),
        ConfigSha256 = new string('0', 64),
        TargetIdentitySha256 = new string('0', 64)
    };

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
