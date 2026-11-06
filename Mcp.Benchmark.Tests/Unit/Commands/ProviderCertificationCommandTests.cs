using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Commands;

public sealed class ProviderCertificationCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mcpval-provider-command-{Guid.NewGuid():N}");
    private readonly string? _previousAttestationKey = Environment.GetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM");
    private readonly int _previousExitCode = Environment.ExitCode;

    [Fact]
    public async Task ExecuteAsync_WithTrustedCompleteProviderEvidence_ShouldEmitEligibleSignedResult()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "output");
        var session = CreateSessionContext();
        var governance = new ExecutionGovernanceService();
        var endpoint = "https://1.1.1.1/mcp";
        var configuration = CreateProviderConfiguration(endpoint, output);
        session.ApplyExecutionPolicy(configuration.Execution!);
        var plan = governance.BuildValidationPlan(session, configuration, output);
        plan.ProviderExecutionPlanSha256 = ValidateCommand.ComputeProviderExecutionPlanDigest(plan);

        using var authorizationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attestationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Environment.SetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM", attestationKey.ExportECPrivateKeyPem());
        var grant = SignGrant(authorizationKey, plan.TargetDigest, plan.PlannedChecks);
        var harness = CreateHarness(plan, grant, authorizationKey);
        var harnessPath = Path.Combine(_root, "provider-harness.json");
        var trustedKeyPath = Path.Combine(_root, "provider-authorization.pem");
        var conformancePath = Path.Combine(_root, "official-conformance.json");
        await File.WriteAllTextAsync(harnessPath, JsonSerializer.Serialize(harness, ArtifactJsonOptions.Create()));
        await File.WriteAllTextAsync(trustedKeyPath, authorizationKey.ExportSubjectPublicKeyInfoPem());
        await File.WriteAllTextAsync(conformancePath, SerializeOfficialConformance(plan));

        var validationResult = CreateCompleteRawResult(endpoint, plan.ResolvedSchemaVersion);
        var validator = new Mock<IMcpValidatorService>();
        validator.Setup(service => service.ValidateServerAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);
        var reportGenerator = new Mock<IReportGenerator>();
        reportGenerator.Setup(generator => generator.GenerateReport(It.IsAny<ValidationResult>())).Returns("provider report");
        var renderer = new Mock<IValidationReportRenderer>();
        renderer.Setup(value => value.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>())).Returns("<html></html>");
        renderer.Setup(value => value.GenerateXmlReport(It.IsAny<ValidationResult>(), It.IsAny<bool>())).Returns("<assurance />");
        renderer.Setup(value => value.GenerateSarifReport(It.IsAny<ValidationResult>())).Returns("{}");
        renderer.Setup(value => value.GenerateJunitReport(It.IsAny<ValidationResult>())).Returns("<testsuites />");
        var command = new ValidateCommand(
            validator.Object,
            new Mock<IConsoleOutputService>(MockBehavior.Loose).Object,
            new Mock<Mcp.Benchmark.ClientProfiles.IClientProfileEvaluator>(MockBehavior.Loose).Object,
            reportGenerator.Object,
            renderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            NullLogger<ValidateCommand>.Instance,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            governance,
            new NoOpModelEvaluationExecutor(),
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object,
            new Mock<IMcpHttpClient>(MockBehavior.Loose).Object,
            session,
            officialConformanceEvidenceImporter: new OfficialConformanceEvidenceImporter());

        await command.ExecuteAsync(
            server: endpoint,
            outputDirectory: new DirectoryInfo(output),
            specProfile: null,
            configFile: null,
            verbose: false,
            policyMode: ValidationPolicyModes.Advisory,
            assuranceProfile: "provider",
            executionMode: "standard",
            signAttestation: true,
            conformanceResultsFile: new FileInfo(conformancePath),
            providerHarnessFile: new FileInfo(harnessPath),
            providerAuthorizationKeyFile: new FileInfo(trustedKeyPath));

        Environment.ExitCode.Should().Be(0);
        var resultPath = Directory.GetFiles(output, "*-result.json").Should().ContainSingle().Subject;
        var attestationPath = Directory.GetFiles(output, "*-attestation.json").Should().ContainSingle().Subject;
        var result = JsonSerializer.Deserialize<ValidationResult>(
            await File.ReadAllTextAsync(resultPath),
            ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true));
        var attestation = JsonSerializer.Deserialize<ValidationAttestation>(
            await File.ReadAllTextAsync(attestationPath),
            ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true));

        result.Should().NotBeNull();
        result!.Assurance.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
        result.Assurance.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Conformant);
        result.Assurance.Assessments.SecurityAssurance.Status.Should().Be(SecurityAssuranceStatus.Verified);
        result.Assurance.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Ready);
        result.Assurance.Assessments.ExternalConformance.Status.Should().Be(ExternalConformanceStatus.Verified);
        result.Run.TargetIdentitySha256.Should().Be(plan.TargetDigest);
        result.Run.ExecutionPlanSha256.Should().Be(plan.ProviderExecutionPlanSha256);
        result.Run.ExecutionMode.Should().Be(ExecutionMode.Standard);
        attestation.Should().NotBeNull();
        ValidationAttestationSigner.Verify(attestation!, await File.ReadAllBytesAsync(resultPath)).Should().BeTrue();
    }

    private static McpValidatorConfiguration CreateProviderConfiguration(string endpoint, string output) => new()
    {
        Server = new McpServerConfig { Endpoint = endpoint, Transport = "http" },
        Assurance = new AssurancePolicyConfig { Profile = "provider" },
        Policy = new ValidationPolicyConfig { Mode = ValidationPolicyModes.Advisory },
        Execution = new ExecutionPolicy { Mode = ExecutionMode.Standard },
        Reporting = new ReportingConfig { OutputDirectory = output, SignAttestation = true }
    };

    private static ProviderAuthorizationGrant SignGrant(
        ECDsa key,
        string targetDigest,
        IReadOnlyList<string> allowedProbeClasses)
    {
        var keyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        var unsigned = new ProviderAuthorizationGrant
        {
            GrantId = "provider-grant",
            Authority = "provider-security-owner",
            TargetIdentitySha256 = targetDigest,
            ValidFromUtc = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            AllowedExecutionModes = [ExecutionMode.Standard],
            AllowedProbeClasses = allowedProbeClasses,
            GrantDocumentSha256 = string.Empty,
            SigningKeyId = keyId,
            SignatureBase64 = string.Empty
        };
        var digest = ProviderAuthorizationVerifier.ComputeGrantDigest(unsigned);
        return new ProviderAuthorizationGrant
        {
            GrantId = unsigned.GrantId,
            Authority = unsigned.Authority,
            TargetIdentitySha256 = unsigned.TargetIdentitySha256,
            ValidFromUtc = unsigned.ValidFromUtc,
            ExpiresAtUtc = unsigned.ExpiresAtUtc,
            AllowedExecutionModes = unsigned.AllowedExecutionModes,
            AllowedProbeClasses = unsigned.AllowedProbeClasses,
            GrantDocumentSha256 = digest,
            SigningKeyId = keyId,
            SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(digest),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        };
    }

    private static ProviderHarnessManifest CreateHarness(
        ExecutionPlan plan,
        ProviderAuthorizationGrant grant,
        ECDsa key)
    {
        var now = DateTimeOffset.UtcNow;
        var unsigned = new ProviderHarnessManifest
        {
            ProviderId = "controlled-provider",
            TargetIdentitySha256 = plan.TargetDigest,
            ProtocolRevision = plan.ResolvedSchemaVersion,
            Authorization = grant,
            RequiredRunCount = 3,
            MinimumConsistency = 0.99,
            Runs =
            [
                CreatePriorRun("prior-1", plan.ProviderExecutionPlanSha256, now.AddMinutes(-20)),
                CreatePriorRun("prior-2", plan.ProviderExecutionPlanSha256, now.AddMinutes(-10))
            ],
            SigningKeyId = grant.SigningKeyId,
            ManifestDocumentSha256 = string.Empty,
            SignatureBase64 = string.Empty
        };
        var digest = ProviderAuthorizationVerifier.ComputeManifestDigest(unsigned);
        return new ProviderHarnessManifest
        {
            ProviderId = unsigned.ProviderId,
            TargetIdentitySha256 = unsigned.TargetIdentitySha256,
            ProtocolRevision = unsigned.ProtocolRevision,
            Authorization = unsigned.Authorization,
            RequiredRunCount = unsigned.RequiredRunCount,
            MinimumConsistency = unsigned.MinimumConsistency,
            Runs = unsigned.Runs,
            SignatureAlgorithm = unsigned.SignatureAlgorithm,
            SigningKeyId = unsigned.SigningKeyId,
            ManifestDocumentSha256 = digest,
            SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(digest),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        };
    }

    private static ProviderHarnessRunReceipt CreatePriorRun(string id, string planDigest, DateTimeOffset started) => new()
    {
        RunId = id,
        ResultSha256 = new string(id[^1], 64),
        ExecutionPlanSha256 = planDigest,
        StartedAtUtc = started,
        EndedAtUtc = started.AddSeconds(1),
        CoreConformance = CoreConformanceStatus.Conformant,
        SecurityAssurance = SecurityAssuranceStatus.Verified,
        OperationalReadiness = OperationalReadinessStatus.Ready
    };

    private static ValidationResult CreateCompleteRawResult(string endpoint, string protocolVersion)
    {
        var result = new ValidationResult
        {
            ValidationId = "provider-current",
            StartTime = DateTime.UtcNow.AddSeconds(-1),
            EndTime = DateTime.UtcNow,
            OverallStatus = ValidationStatus.Passed,
            ProtocolVersion = protocolVersion,
            ServerConfig = new McpServerConfig { Endpoint = endpoint, Transport = "http", ProtocolVersion = protocolVersion },
            ValidationConfig = new McpValidatorConfiguration(),
            ProtocolCompliance = new ComplianceTestResult(),
            SecurityTesting = new SecurityTestResult
            {
                AttackSimulations =
                [
                    new AttackSimulationResult
                    {
                        AttackVector = "controlled-security",
                        Description = "Controlled security probe passed.",
                        Authority = ValidationRuleSource.Guideline,
                        Outcome = ValidationOutcome.Succeeded,
                        Gate = GateOutcome.Note,
                        DefenseSuccessful = true
                    }
                ]
            },
            PerformanceTesting = new PerformanceTestResult()
        };
        AddCovered(result, "protocol-core", "json-rpc");
        AddCovered(result, "security-controls", "controlled-security");
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

    private static string SerializeOfficialConformance(ExecutionPlan plan)
    {
        var checks = new[]
        {
            new OfficialConformanceCheckRecord
            {
                Id = "required-provider-check",
                Name = "required-provider-check",
                Description = "Required provider behavior.",
                Status = OfficialConformanceCheckStatus.Success,
                Timestamp = DateTimeOffset.UtcNow,
                SpecReferences = [new OfficialConformanceSpecReference { Id = "MCP-PROVIDER" }]
            }
        };
        var document = new OfficialConformanceEvidenceDocument
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
                IdentitySha256 = plan.TargetDigest,
                Transport = plan.Transport,
                ProtocolVersion = plan.ResolvedSchemaVersion
            },
            Invocation = new OfficialConformanceInvocation { Mode = "server" },
            Run = new OfficialConformanceRunRecord
            {
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-2),
                EndedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                ExitCode = 0,
                State = OfficialConformanceRunState.Completed
            },
            Scenarios = CreateOfficialScenarioSet(checks)
        };
        return JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions());
    }

    private static IReadOnlyList<OfficialConformanceScenarioRecord> CreateOfficialScenarioSet(
        IReadOnlyList<OfficialConformanceCheckRecord> checks)
    {
        var scenarios = OfficialConformanceContract.RequiredServerScenarios
            .Select(scenarioId => CreateOfficialScenario(scenarioId, checks))
            .ToList();
        scenarios.AddRange(OfficialConformanceContract.NotScoredServerScenarios.Select(scenario =>
            CreateOfficialScenario(
                scenario.ScenarioId,
                checks,
                OfficialConformanceScoringDisposition.NotScored,
                scenario.Reason,
                scenario.ExtensionId)));
        return scenarios;
    }

    private static OfficialConformanceScenarioRecord CreateOfficialScenario(
        string scenarioId,
        IReadOnlyList<OfficialConformanceCheckRecord> checks,
        OfficialConformanceScoringDisposition disposition = OfficialConformanceScoringDisposition.Required,
        string? reason = null,
        string? extensionId = null)
    {
        var checksJson = OfficialConformanceContract.SerializeChecks(checks);
        return new OfficialConformanceScenarioRecord
        {
            ScenarioId = scenarioId,
            ScoringDisposition = disposition,
            NotScoredReason = reason,
            ExtensionId = extensionId,
            ChecksSha256 = OfficialConformanceContract.ComputeChecksSha256(checksJson),
            ChecksBase64 = Convert.ToBase64String(checksJson)
        };
    }

    private CliSessionContext CreateSessionContext()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var state = Path.Combine(_root, "state");
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(logs);
        return new CliSessionContext(sessionId, _root, state, logs, Path.Combine(logs, "session.log"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MCPVAL_ATTESTATION_PRIVATE_KEY_PEM", _previousAttestationKey);
        Environment.ExitCode = _previousExitCode;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
