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
    [Theory]
    [InlineData("PackageName")]
    [InlineData("Version")]
    [InlineData("SourceCommit")]
    [InlineData("PackageSha256")]
    [InlineData("PackageIntegrity")]
    public void OfficialRunnerPin_ShouldRequireEveryExactField(string field)
    {
        var document = CreateDocument([CreateCheck("pin-check", OfficialConformanceCheckStatus.Success)]);
        OfficialConformanceContract.IsPinnedRunner(document.Runner).Should().BeTrue();
        var node = JsonSerializer.SerializeToNode(document.Runner)!;
        node[field] = node[field]!.GetValue<string>() + "changed";
        OfficialConformanceContract.IsPinnedRunner(node.Deserialize<OfficialConformanceRunnerIdentity>()!).Should().BeFalse();
    }

    [Theory]
    [InlineData("Revision")]
    [InlineData("Sha256")]
    public void OfficialRequirementPin_ShouldRequireEveryExactField(string field)
    {
        var document = CreateDocument([CreateCheck("pin-check", OfficialConformanceCheckStatus.Success)]);
        OfficialConformanceContract.IsPinnedRequirementSet(document.RequirementSet).Should().BeTrue();
        var node = JsonSerializer.SerializeToNode(document.RequirementSet)!;
        node[field] = node[field]!.GetValue<string>() + "changed";
        OfficialConformanceContract.IsPinnedRequirementSet(node.Deserialize<OfficialConformanceRequirementSet>()!).Should().BeFalse();
    }

    [Theory]
    [InlineData("scenario-null")]
    [InlineData("scenario-entry-null")]
    [InlineData("scenario-id-null")]
    [InlineData("scenario-id-empty")]
    [InlineData("scenario-id-blank")]
    [InlineData("scenario-id-long")]
    [InlineData("checks-null")]
    [InlineData("check-null")]
    [InlineData("check-id-null")]
    [InlineData("check-id-empty")]
    [InlineData("check-id-blank")]
    [InlineData("check-id-long")]
    public void RetainedAssessment_ShouldRejectMalformedInventory(string fault)
    {
        var node = CreateRetainedAssessmentNode();
        var scenario = node["Scenarios"]![0]!;
        var check = scenario["Checks"]![0]!;
        switch (fault)
        {
            case "scenario-null": node["Scenarios"] = null; break;
            case "scenario-entry-null": node["Scenarios"]![0] = null; break;
            case "scenario-id-null": scenario["ScenarioId"] = null; break;
            case "scenario-id-empty": scenario["ScenarioId"] = ""; break;
            case "scenario-id-blank": scenario["ScenarioId"] = " "; break;
            case "scenario-id-long": scenario["ScenarioId"] = new string('a', OfficialConformanceContract.MaximumTextLength + 1); break;
            case "checks-null": scenario["Checks"] = null; break;
            case "check-null": scenario["Checks"]![0] = null; break;
            case "check-id-null": check["Id"] = null; break;
            case "check-id-empty": check["Id"] = ""; break;
            case "check-id-blank": check["Id"] = " "; break;
            case "check-id-long": check["Id"] = new string('a', OfficialConformanceContract.MaximumTextLength + 1); break;
        }
        var restore = () => node.Deserialize<OfficialConformanceAssessment>();
        restore.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("scenarios", false)]
    [InlineData("scenarios", true)]
    [InlineData("checks", false)]
    [InlineData("checks", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    public void RetainedAssessment_ShouldEnforceExactInventoryLimits(string kind, bool overLimit)
    {
        var node = CreateRetainedAssessmentNode();
        var scenarios = node["Scenarios"]!.AsArray();
        var scenario = scenarios[0]!;
        if (kind == "scenarios")
        {
            var template = scenario.DeepClone();
            scenarios.Clear();
            for (var index = 0; index < OfficialConformanceContract.MaximumScenarios + (overLimit ? 1 : 0); index++)
            {
                var entry = template.DeepClone();
                entry["ScenarioId"] = $"scenario-{index}";
                scenarios.Add(entry);
            }
        }
        else if (kind == "checks")
        {
            var checks = scenario["Checks"]!.AsArray();
            var template = checks[0]!.DeepClone();
            checks.Clear();
            for (var index = 0; index < OfficialConformanceContract.MaximumChecksPerScenario + (overLimit ? 1 : 0); index++)
            {
                var entry = template.DeepClone();
                entry["SourceIndex"] = index;
                checks.Add(entry);
            }
        }
        else
        {
            scenario["ScenarioId"] = new string('s', OfficialConformanceContract.MaximumTextLength + (overLimit ? 1 : 0));
            scenario["Checks"]![0]!["Id"] = new string('c', OfficialConformanceContract.MaximumTextLength + (overLimit ? 1 : 0));
        }
        var restore = () => node.Deserialize<OfficialConformanceAssessment>();
        if (overLimit) restore.Should().Throw<JsonException>();
        else restore.Should().NotThrow().Which.Should().NotBeNull();
    }

    [Theory]
    [InlineData(OfficialConformanceCheckStatus.Failure, AssuranceApplicability.Applicable, true, true)]
    [InlineData(OfficialConformanceCheckStatus.Warning, AssuranceApplicability.Applicable, true, true)]
    [InlineData(OfficialConformanceCheckStatus.Success, AssuranceApplicability.Applicable, true, false)]
    [InlineData(OfficialConformanceCheckStatus.Info, AssuranceApplicability.Applicable, true, false)]
    [InlineData(OfficialConformanceCheckStatus.Skipped, AssuranceApplicability.Applicable, true, false)]
    [InlineData(OfficialConformanceCheckStatus.Info, AssuranceApplicability.NotApplicable, false, true)]
    [InlineData(OfficialConformanceCheckStatus.Failure, AssuranceApplicability.NotApplicable, false, false)]
    [InlineData(OfficialConformanceCheckStatus.Warning, AssuranceApplicability.NotApplicable, false, false)]
    [InlineData(OfficialConformanceCheckStatus.Success, AssuranceApplicability.NotApplicable, false, false)]
    [InlineData(OfficialConformanceCheckStatus.Skipped, AssuranceApplicability.NotApplicable, false, false)]
    [InlineData((OfficialConformanceCheckStatus)999, AssuranceApplicability.Applicable, false, false)]
    public void RetainedAssessment_ShouldValidateIndependentPrerequisiteAndApplicabilityFields(
        OfficialConformanceCheckStatus status, AssuranceApplicability applicability, bool missing, bool valid)
    {
        var node = CreateRetainedAssessmentNode();
        var check = node["Scenarios"]![0]!["Checks"]![0]!;
        check["Status"] = (int)status;
        check["Applicability"] = (int)applicability;
        check["PrerequisiteMissing"] = missing;
        var restore = () => node.Deserialize<OfficialConformanceAssessment>();
        if (!valid) restore.Should().Throw<JsonException>();
        else
        {
            var actual = restore()!.Scenarios[0].Checks[0];
            actual.Status.Should().Be(status);
            actual.Applicability.Should().Be(applicability);
            actual.PrerequisiteMissing.Should().Be(missing);
        }
    }

    private System.Text.Json.Nodes.JsonNode CreateRetainedAssessmentNode() => JsonSerializer.SerializeToNode(
        _importer.Import(Serialize(CreateDocument([CreateCheck("guard-check", OfficialConformanceCheckStatus.Success)]))).Assessment)!;

    [Theory]
    [InlineData(false, CoreConformanceStatus.Nonconformant)]
    [InlineData(true, CoreConformanceStatus.Incomplete)]
    public void UntestableOfficialCheck_ShouldRemainMissingEvidenceInsteadOfTargetViolation(bool untestable, CoreConformanceStatus expected)
    {
        var document = CreateDocument([new OfficialConformanceCheckRecord
        {
            Id = "required-check", Name = "Required check", Description = "Required behavior", Status = OfficialConformanceCheckStatus.Failure,
            Timestamp = DateTimeOffset.UtcNow, Details = JsonSerializer.SerializeToElement(new { untestable, reason = "Fixture prerequisite" })
        }]);
        var imported = _importer.Import(Serialize(document));
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = imported.Assessment;
        result.Evidence.Coverage.AddRange(imported.Coverage);
        var evaluation = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        imported.Assessment.Failed.Should().Be(1, "the original runner failure count remains visible");
        evaluation.Assessments.CoreConformance.Status.Should().Be(expected);
        evaluation.Assessments.ExternalConformance.Status.Should().Be(untestable ? ExternalConformanceStatus.Incomplete : ExternalConformanceStatus.Nonconformant);
        evaluation.Assessments.ExternalConformance.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Verified);
        var check = imported.Assessment.Scenarios.SelectMany(scenario => scenario.Checks).Single(check => check.Id == "required-check");
        check.Status.Should().Be(OfficialConformanceCheckStatus.Failure);
        check.PrerequisiteMissing.Should().Be(untestable);
    }

    [Fact]
    public void OfficialFailure_ShouldNotBeHiddenByContradictoryApplicability()
    {
        var imported = _importer.Import(Serialize(CreateDocument([CreateCheck("failure", OfficialConformanceCheckStatus.Failure)])));
        var result = new ValidationResult();
        var node = JsonSerializer.SerializeToNode(imported.Assessment, OfficialConformanceContract.CreateJsonOptions())!;
        var check = node["scenarios"]!.AsArray().SelectMany(scenario => scenario!["checks"]!.AsArray())
            .Single(candidate => candidate!["id"]!.GetValue<string>() == "failure")!;
        check["applicability"] = "notApplicable";
        var restore = () => node.Deserialize<OfficialConformanceAssessment>(OfficialConformanceContract.CreateJsonOptions());
        restore.Should().Throw<JsonException>();
        result.Assessments.OfficialConformance = imported.Assessment;

        var evaluation = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Nonconformant);
    }
    [Fact]
    public void OfficialCheck_ShouldRejectUndefinedApplicability()
    {
        var create = () => new OfficialConformanceCheckAssessment
        {
            Id = "invalid", Name = "Invalid", Description = "Undefined applicability", Applicability = (AssuranceApplicability)999
        };
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("json", true)]
    [InlineData("sse", false)]
    [InlineData("missing-details", false)]
    [InlineData("duplicate-index", false)]
    [InlineData("missing-parent", false)]
    [InlineData("unpinned", false)]
    public void OptionalSseInfo_ShouldUsePinnedStructuredApplicabilityWithoutPassCredit(string variant, bool notApplicable)
    {
        var document = CreateDocument([CreateCheck("ordinary", OfficialConformanceCheckStatus.Success)], runnerVersion: variant == "unpinned" ? "untrusted" : null);
        var node = JsonSerializer.SerializeToNode(document, OfficialConformanceContract.CreateJsonOptions())!;
        var scenario = node["scenarios"]!.AsArray().Single(item => item!["scenarioId"]!.GetValue<string>() == "server-sse-multiple-streams")!;
        var checks = JsonSerializer.SerializeToNode(new[]
        {
            CreateCheck("server-accepts-multiple-post-streams", OfficialConformanceCheckStatus.Success),
            CreateCheck("server-sse-streams-functional", OfficialConformanceCheckStatus.Info)
        }, OfficialConformanceContract.CreateJsonOptions())!.AsArray();
        if (variant != "missing-details") checks[1]!["details"] = JsonSerializer.SerializeToNode(new
        {
            numSseStreams = variant == "sse" ? 1 : 0,
            results = Enumerable.Range(0, 3).Select(index => new { index = variant == "duplicate-index" ? 0 : index, type = "json", skipped = true })
        });
        if (variant == "missing-parent") checks.RemoveAt(0);
        var bytes = Encoding.UTF8.GetBytes(checks.ToJsonString());
        scenario["checksBase64"] = Convert.ToBase64String(bytes);
        scenario["checksSha256"] = OfficialConformanceContract.ComputeChecksSha256(bytes);

        var imported = _importer.Import(node.ToJsonString());
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = imported.Assessment;
        var observed = imported.Assessment.Scenarios.Single(item => item.ScenarioId == "server-sse-multiple-streams");
        var info = observed.Checks.Single(check => check.Id == "server-sse-streams-functional");
        info.Status.Should().Be(OfficialConformanceCheckStatus.Info);
        info.Applicability.Should().Be(notApplicable ? AssuranceApplicability.NotApplicable : AssuranceApplicability.Applicable);
        var claim = Mcp.Benchmark.Core.Services.AssuranceClaimProjector.Project(result).Single(claim => claim.EvidenceIds.Contains(
            OfficialConformanceContract.GetCheckEvidenceId(observed.ScenarioId, info.Id, info.SourceIndex)));
        claim.Outcome.Should().Be(notApplicable ? AssuranceClaimOutcome.NotApplicable : variant == "unpinned" ? AssuranceClaimOutcome.Incomplete : AssuranceClaimOutcome.ReviewRequired);
        claim.Outcome.Should().NotBe(AssuranceClaimOutcome.Satisfied);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_RepeatedCheckIds_ShouldPreserveEveryAssertionAndFailure(bool failureFirst)
    {
        var document = CreateDocument([
            CreateCheck("duplicate", failureFirst ? OfficialConformanceCheckStatus.Failure : OfficialConformanceCheckStatus.Success),
            CreateCheck("duplicate", failureFirst ? OfficialConformanceCheckStatus.Success : OfficialConformanceCheckStatus.Failure)
        ]);

        var imported = _importer.Import(Serialize(document));
        var result = new ValidationResult();
        result.Assessments.OfficialConformance = imported.Assessment;
        result.Evidence.Coverage.AddRange(imported.Coverage);
        var checks = imported.Assessment.Scenarios.SelectMany(scenario => scenario.Checks).Where(check => check.Id == "duplicate").ToArray();
        checks.Select(check => check.SourceIndex).Should().Equal(0, 1);
        imported.Observations.Select(observation => observation.Id).Should().OnlyHaveUniqueItems();
        imported.Coverage.Select(coverage => coverage.Scope).Should().OnlyHaveUniqueItems();
        var evaluation = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive);
        evaluation.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Nonconformant);
        var claims = evaluation.Claims.Where(claim => claim.Authority == AssuranceAuthority.OfficialConformance).ToArray();
        claims.Should().ContainSingle(claim => claim.Outcome == AssuranceClaimOutcome.Violated);
        claims.SelectMany(claim => claim.EvidenceIds).Should().OnlyContain(id => imported.Observations.Any(observation => observation.Id == id));
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

    [Theory]
    [InlineData("duplicate", false)]
    [InlineData("duplicate", true)]
    [InlineData("legacy-collision", false)]
    [InlineData("legacy-collision", true)]
    [InlineData("negative", false)]
    [InlineData("too-large", false)]
    [InlineData("prerequisite-info", false)]
    [InlineData("applicability-failure", false)]
    public void SavedAssessment_InvalidCheckMetadata_ShouldRejectBeforeProjection(string mutation, bool failureFirst)
    {
        var imported = _importer.Import(Serialize(CreateDocument([
            CreateCheck("duplicate", failureFirst ? OfficialConformanceCheckStatus.Failure : OfficialConformanceCheckStatus.Success),
            CreateCheck("duplicate", failureFirst ? OfficialConformanceCheckStatus.Success : OfficialConformanceCheckStatus.Failure)
        ])));
        var node = JsonSerializer.SerializeToNode(imported.Assessment)!;
        var checks = node["Scenarios"]!.AsArray().Single(scenario =>
            scenario!["Checks"]!.AsArray().Any(check => check!["Id"]!.GetValue<string>() == "duplicate"))!["Checks"]!;
        if (mutation == "duplicate") checks[1]!["SourceIndex"] = 0;
        if (mutation == "legacy-collision")
        {
            checks[0]!["SourceIndex"] = null;
            checks[1]!["SourceIndex"] = null;
            checks[0]!["Id"] = "rule-a";
            checks[1]!["Id"] = "rule_a";
        }
        if (mutation == "negative") checks[0]!["SourceIndex"] = -1;
        if (mutation == "too-large") checks[0]!["SourceIndex"] = OfficialConformanceContract.MaximumChecksPerScenario;
        if (mutation == "prerequisite-info")
        {
            checks[0]!["Status"] = (int)OfficialConformanceCheckStatus.Info;
            checks[0]!["Applicability"] = (int)AssuranceApplicability.NotApplicable;
            checks[0]!["PrerequisiteMissing"] = true;
        }
        if (mutation == "applicability-failure")
            checks[1]!["Applicability"] = (int)AssuranceApplicability.NotApplicable;

        var action = () => node.Deserialize<OfficialConformanceAssessment>();

        action.Should().Throw<JsonException>();
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

    internal static OfficialConformanceEvidenceDocument CreateDocument(
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

    internal static OfficialConformanceCheckRecord CreateCheck(
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