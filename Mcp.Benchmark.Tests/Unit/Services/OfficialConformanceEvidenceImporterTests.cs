using System.Text.Json;
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
        result.Assessment.Passed.Should().Be(1);
        result.Assessment.CreditedPassed.Should().Be(1);
        result.Assessment.Failed.Should().Be(1);
        result.Assessment.EvidenceSummary.Covered.Should().Be(2);
        result.Coverage.Should().OnlyContain(item =>
            item.Lane == EvaluationLane.ExternalConformance &&
            item.Origin == EvidenceOrigin.ExternalConformanceCheck);
        result.Assessment.Scenarios.Single().Checks.Single(check => check.Id == "required-fail")
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
        result.Assessment.EvidenceSummary.Inconclusive.Should().Be(1);
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
    public void Import_TamperedChecks_ShouldRejectEvidence()
    {
        var document = CreateDocument([CreateCheck("required-pass", OfficialConformanceCheckStatus.Success)]);
        var json = Serialize(document).Replace("Required check", "Changed check", StringComparison.Ordinal);

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

    private static OfficialConformanceEvidenceDocument CreateDocument(
        IReadOnlyList<OfficialConformanceCheckRecord> checks,
        string? runnerVersion = null,
        OfficialConformanceRunState runState = OfficialConformanceRunState.Completed)
    {
        var scenario = new OfficialConformanceScenarioRecord
        {
            ScenarioId = "server-initialize",
            ChecksSha256 = OfficialConformanceContract.ComputeChecksSha256(checks),
            Checks = checks
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
            Scenarios = [scenario]
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