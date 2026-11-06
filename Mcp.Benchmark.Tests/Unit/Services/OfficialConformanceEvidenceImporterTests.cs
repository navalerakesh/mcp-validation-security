using System.Text.Json;
using System.Text;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Exceptions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class OfficialConformanceEvidenceImporterTests
{
    private readonly OfficialConformanceEvidenceImporter _importer = new();

    [Fact]
    public void Import_PinnedEvidence_ShouldProjectVerifiedExternalLaneWithoutRawError()
    {
        var document = CreateDocument([
            CreateCheck("required-pass", OfficialConformanceCheckStatus.Success),
            CreateCheck("required-fail", OfficialConformanceCheckStatus.Failure, "Bearer secret-value")
        ]);

        var result = _importer.Import(Serialize(document), new OfficialConformanceImportContext
        {
            ExpectedTargetIdentitySha256 = document.Target.IdentitySha256,
            ExpectedProtocolVersion = document.Target.ProtocolVersion
        });

        result.Assessment.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Verified);
        result.Assessment.SourceEvidenceSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        result.Assessment.Passed.Should().Be(
            OfficialConformanceContract.RequiredServerScenarios.Count +
            OfficialConformanceContract.NotScoredServerScenarios.Count);
        result.Assessment.CreditedPassed.Should().Be(OfficialConformanceContract.RequiredServerScenarios.Count);
        result.Assessment.Failed.Should().Be(1);
        result.Assessment.EvidenceSummary.Covered.Should().Be(
            OfficialConformanceContract.RequiredServerScenarios.Count +
            OfficialConformanceContract.NotScoredServerScenarios.Count + 1);
        result.Coverage.Should().OnlyContain(item =>
            item.Lane == EvaluationLane.ExternalConformance &&
            item.Origin == EvidenceOrigin.ExternalConformanceCheck);
        result.Assessment.Scenarios.SelectMany(scenario => scenario.Checks).Single(check => check.Id == "required-fail")
            .Should().Match<OfficialConformanceCheckAssessment>(check =>
                check.HasErrorDetail &&
                check.ErrorDetailSha256 != null &&
                !check.ErrorDetailSha256.Contains("secret", StringComparison.Ordinal));
        Serialize(result.Assessment).Should().NotContain("secret-value");
    }

    [Fact]
    public void Import_UnpinnedRunner_ShouldRemainUnverifiedAndEarnNoPassCredit()
    {
        var document = CreateDocument([CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)], runnerVersion: "future");

        var result = _importer.Import(Serialize(document));

        result.Assessment.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Unverified);
        result.Assessment.CreditedPassed.Should().Be(0);
        result.Assessment.EvidenceSummary.Covered.Should().Be(0);
        result.Assessment.EvidenceSummary.Inconclusive.Should().Be(
            OfficialConformanceContract.RequiredServerScenarios.Count +
            OfficialConformanceContract.NotScoredServerScenarios.Count);
    }

    [Fact]
    public void Import_PinnedIncompleteRun_ShouldEarnNoPassCredit()
    {
        var document = CreateDocument(
            [CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)],
            runState: OfficialConformanceRunState.TimedOut);

        var result = _importer.Import(Serialize(document));

        result.Assessment.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Incomplete);
        result.Assessment.Passed.Should().Be(1);
        result.Assessment.CreditedPassed.Should().Be(0);
        result.Assessment.EvidenceSummary.Covered.Should().Be(0);
        result.Assessment.EvidenceSummary.Inconclusive.Should().Be(1);
    }

    [Fact]
    public void Import_ExactOfficialCheckBytes_ShouldParseAfterRawDigestVerification()
    {
        var checksJson = Encoding.UTF8.GetBytes("""
            [{"id":"wire-schema-valid","name":"Wire schema","description":"Wire messages match the schema.","status":"SUCCESS","timestamp":"2026-11-04T10:00:00.000Z","specReferences":[]}]
            """);
        var document = CreateDocument(
            [CreateCheck("unused", OfficialConformanceCheckStatus.Info)],
            runState: OfficialConformanceRunState.TimedOut,
            primaryChecksJson: checksJson);

        var result = _importer.Import(Serialize(document));

        result.Assessment.Scenarios.Single().Checks.Should().ContainSingle()
            .Which.Status.Should().Be(OfficialConformanceCheckStatus.Success);
        document.Scenarios[0].ChecksSha256.Should().Be(OfficialConformanceContract.ComputeChecksSha256(checksJson));
    }

    [Fact]
    public void Import_TamperedChecks_ShouldRejectEvidence()
    {
        var document = CreateDocument([CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)]);
        var json = Serialize(document).Replace(
            document.Scenarios[0].ChecksSha256,
            new string('0', 64),
            StringComparison.Ordinal);

        var action = () => _importer.Import(json);

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*do not match their recorded digest*");
    }

    [Fact]
    public void Import_DuplicateCheckIds_ShouldRejectEvidence()
    {
        var document = CreateDocument([
            CreateCheck("duplicate", OfficialConformanceCheckStatus.Success),
            CreateCheck("duplicate", OfficialConformanceCheckStatus.Success)
        ]);

        var action = () => _importer.Import(Serialize(document));

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*duplicate check IDs*");
    }

    [Fact]
    public void Import_TargetMismatch_ShouldRejectEvidence()
    {
        var document = CreateDocument([CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)]);

        var action = () => _importer.Import(Serialize(document), new OfficialConformanceImportContext
        {
            ExpectedTargetIdentitySha256 = new string('b', 64)
        });

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*does not match the validation target identity*");
    }

    [Fact]
    public void Import_CompletedRunMissingPinnedScenarios_ShouldRejectEvidence()
    {
        var document = CreateDocument(
            [CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)],
            includeCompleteScenarioSet: false);

        var action = () => _importer.Import(Serialize(document));

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*missing pinned scenarios*");
    }

    [Fact]
    public void Import_UnexpectedPinnedScenario_ShouldRejectEvidence()
    {
        var document = CreateDocument(
            [CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)],
            scenarioId: "unexpected-scenario");

        var action = () => _importer.Import(Serialize(document));

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*outside the pinned requirement set*");
    }

    [Fact]
    public void Import_MisclassifiedRequiredScenario_ShouldRejectEvidence()
    {
        var document = CreateDocument(
            [CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)],
            disposition: OfficialConformanceScoringDisposition.NotScored,
            notScoredReason: "extension");

        var action = () => _importer.Import(Serialize(document));

        action.Should().Throw<OfficialConformanceEvidenceException>()
            .WithMessage("*misclassifies a required scenario*");
    }

    private static OfficialConformanceEvidenceDocument CreateDocument(
        IReadOnlyList<OfficialConformanceCheckRecord> checks,
        string? runnerVersion = null,
        OfficialConformanceRunState runState = OfficialConformanceRunState.Completed,
        bool includeCompleteScenarioSet = true,
        string? scenarioId = null,
        OfficialConformanceScoringDisposition disposition = OfficialConformanceScoringDisposition.Required,
        string? notScoredReason = null,
        byte[]? primaryChecksJson = null)
    {
        var checksJson = primaryChecksJson ?? OfficialConformanceContract.SerializeChecks(checks);
        var scenario = new OfficialConformanceScenarioRecord
        {
            ScenarioId = scenarioId ?? OfficialConformanceContract.RequiredServerScenarios[0],
            ScoringDisposition = disposition,
            NotScoredReason = notScoredReason,
            ChecksSha256 = OfficialConformanceContract.ComputeChecksSha256(checksJson),
            ChecksBase64 = Convert.ToBase64String(checksJson)
        };
        return new OfficialConformanceEvidenceDocument
        {
            Runner = new OfficialConformanceRunnerIdentity
            {
                PackageName = OfficialConformanceContract.PackageName,
                Version = runnerVersion ?? OfficialConformanceContract.PackageVersion,
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
                IdentitySha256 = new string('a', 64),
                Transport = "http",
                ProtocolVersion = OfficialConformanceContract.RequirementSetRevision
            },
            Invocation = new OfficialConformanceInvocation { Mode = "server" },
            Run = new OfficialConformanceRunRecord
            {
                StartedAtUtc = DateTimeOffset.Parse("2026-11-04T10:00:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-11-04T10:00:01Z"),
                ExitCode = 0,
                State = runState
            },
            Scenarios = runState == OfficialConformanceRunState.Completed && includeCompleteScenarioSet
                ? CreateCompletedScenarioSet(scenario)
                : [scenario]
        };
    }

    private static IReadOnlyList<OfficialConformanceScenarioRecord> CreateCompletedScenarioSet(
        OfficialConformanceScenarioRecord primary)
    {
        var scenarios = new List<OfficialConformanceScenarioRecord> { primary };
        scenarios.AddRange(OfficialConformanceContract.RequiredServerScenarios
            .Skip(1)
            .Select(scenarioId => CreateScenario(scenarioId, OfficialConformanceScoringDisposition.Required)));
        scenarios.AddRange(OfficialConformanceContract.NotScoredServerScenarios
            .Select(scenario => CreateScenario(
                scenario.ScenarioId,
                OfficialConformanceScoringDisposition.NotScored,
                scenario.Reason,
                scenario.ExtensionId)));
        return scenarios;
    }

    private static OfficialConformanceScenarioRecord CreateScenario(
        string scenarioId,
        OfficialConformanceScoringDisposition disposition,
        string? reason = null,
        string? extensionId = null)
    {
        var checks = new[] { CreateCheck($"{scenarioId}-pass", OfficialConformanceCheckStatus.Success) };
        return new OfficialConformanceScenarioRecord
        {
            ScenarioId = scenarioId,
            ScoringDisposition = disposition,
            NotScoredReason = reason,
            ExtensionId = extensionId,
            ChecksSha256 = OfficialConformanceContract.ComputeChecksSha256(OfficialConformanceContract.SerializeChecks(checks)),
            ChecksBase64 = Convert.ToBase64String(OfficialConformanceContract.SerializeChecks(checks))
        };
    }

    private static OfficialConformanceCheckRecord CreateCheck(
        string id,
        OfficialConformanceCheckStatus status,
        string? errorMessage = null) => new()
        {
            Id = id,
            Name = id,
            Description = "Required check",
            Status = status,
            Timestamp = DateTimeOffset.Parse("2026-11-04T10:00:00Z"),
            SpecReferences = [new OfficialConformanceSpecReference { Id = "MCP-1" }],
            ErrorMessage = errorMessage
        };

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, OfficialConformanceContract.CreateJsonOptions());
}