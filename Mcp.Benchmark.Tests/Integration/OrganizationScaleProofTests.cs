using System.Collections.Concurrent;
using FluentAssertions;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Tests.Fixtures;

namespace Mcp.Benchmark.Tests.Integration;

public sealed class OrganizationScaleProofTests
{
    [Fact]
    public async Task ParallelPlansAndAssuranceRuns_ShouldRemainTargetAndEvidenceIsolated()
    {
        const int runCount = 50;
        var outputs = new ConcurrentBag<(string TargetDigest, string PlanDigest, string ClaimId, string ValidationId)>();

        await Parallel.ForEachAsync(Enumerable.Range(0, runCount), async (index, _) =>
        {
            var sessionId = $"scale-session-{index:D3}";
            var session = new CliSessionContext(sessionId);
            var configuration = new McpValidatorConfiguration
            {
                Server = new McpServerConfig
                {
                    Endpoint = $"https://provider-{index:D3}.example.test/mcp",
                    Transport = "http"
                },
                Execution = new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 25 },
                Assurance = new AssurancePolicyConfig { Profile = "ci" }
            };
            session.ApplyExecutionPolicy(configuration.Execution);
            var plan = new ExecutionGovernanceService().BuildValidationPlan(session, configuration, outputDirectory: null);
            var planDigest = ValidateCommand.ComputeProviderExecutionPlanDigest(plan);
            var result = CreateIsolatedResult(index);
            var assurance = AssuranceAssessmentEngine.Evaluate(
                result,
                AssuranceProfileCatalog.Ci,
                new AssuranceEvaluationContext { ExecutionMode = ExecutionMode.Standard });
            outputs.Add((plan.TargetDigest, planDigest, assurance.Claims.Single().ClaimId, result.ValidationId));
            await Task.Yield();
        });

        outputs.Should().HaveCount(runCount);
        outputs.Select(item => item.TargetDigest).Should().OnlyHaveUniqueItems();
        outputs.Select(item => item.PlanDigest).Should().OnlyHaveUniqueItems();
        outputs.Select(item => item.ClaimId).Should().OnlyHaveUniqueItems();
        outputs.Select(item => item.ValidationId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ParallelOfflineRendering_ShouldBeByteDeterministic()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        var renderer = new ValidationReportRenderer();
        var outputs = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() => new
        {
            Html = renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true),
            Xml = renderer.GenerateXmlReport(result, verbose: true),
            Sarif = renderer.GenerateSarifReport(result),
            Junit = renderer.GenerateJunitReport(result)
        })));

        outputs.Select(output => output.Html).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        outputs.Select(output => output.Xml).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        outputs.Select(output => output.Sarif).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        outputs.Select(output => output.Junit).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public void ProviderHarnessEvaluation_ThousandRunSoak_ShouldRemainDeterministicAndStateless()
    {
        var manifest = CreateHarness();
        var verification = new ProviderAuthorizationVerification
        {
            Status = ProviderAuthorizationVerificationStatus.Verified,
            GrantId = manifest.Authorization.GrantId,
            VerifiedKeyId = manifest.Authorization.SigningKeyId
        };
        var assessments = Enumerable.Range(0, 1000)
            .Select(_ => ProviderHarnessEvaluator.Evaluate(
                manifest,
                verification,
                manifest.TargetIdentitySha256,
                manifest.ProtocolRevision,
                new string('e', 64),
                ExecutionMode.Standard,
                new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero),
                manifest.Authorization.AllowedProbeClasses))
            .ToArray();

        assessments.Should().OnlyContain(assessment => assessment.Status == ProviderHarnessStatus.Verified);
        assessments.Select(assessment => assessment.RepeatedRunConsistency).Distinct().Should().Equal(1d);
        manifest.Runs.Should().HaveCount(3);
    }

    private static ValidationResult CreateIsolatedResult(int index)
    {
        return new ValidationResult
        {
            ValidationId = $"validation-{index:D3}",
            ToolValidation = new ToolTestResult
            {
                Findings =
                [
                    new ValidationFinding
                    {
                        RuleId = $"SCALE.RULE.{index:D3}",
                        Source = ValidationRuleSource.Heuristic,
                        Component = $"tool-{index:D3}",
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = "Isolated scale evidence."
                    }
                ]
            }
        };
    }

    private static ProviderHarnessManifest CreateHarness()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        return new ProviderHarnessManifest
        {
            ProviderId = "soak-provider",
            TargetIdentitySha256 = new string('a', 64),
            ProtocolRevision = "2026-07-28",
            Authorization = new ProviderAuthorizationGrant
            {
                GrantId = "soak-grant",
                Authority = "provider-owner",
                TargetIdentitySha256 = new string('a', 64),
                ValidFromUtc = now.AddHours(-1),
                ExpiresAtUtc = now.AddHours(1),
                AllowedExecutionModes = [ExecutionMode.Standard],
                AllowedProbeClasses = ["protocol-active"],
                GrantDocumentSha256 = new string('b', 64),
                SigningKeyId = new string('c', 64),
                SignatureBase64 = "dGVzdA=="
            },
            SigningKeyId = new string('c', 64),
            ManifestDocumentSha256 = new string('b', 64),
            SignatureBase64 = Convert.ToBase64String(new byte[64]),
            Runs = Enumerable.Range(0, 3).Select(index => new ProviderHarnessRunReceipt
            {
                RunId = $"run-{index}",
                ResultSha256 = new string((char)('d' + index), 64),
                ExecutionPlanSha256 = new string('e', 64),
                StartedAtUtc = now.AddMinutes(-10 + index),
                EndedAtUtc = now.AddMinutes(-10 + index).AddSeconds(1),
                CoreConformance = CoreConformanceStatus.Conformant,
                SecurityAssurance = SecurityAssuranceStatus.Verified,
                OperationalReadiness = OperationalReadinessStatus.Ready
            }).ToArray()
        };
    }
}
