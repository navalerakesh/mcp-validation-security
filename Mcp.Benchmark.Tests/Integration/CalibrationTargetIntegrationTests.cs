using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Scoring;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Mcp.Benchmark.Tests.Integration;

public sealed class CalibrationTargetIntegrationTests : IClassFixture<McpServerTestFixture>
{
    private readonly McpServerTestFixture _fixture;
    private readonly ProtocolComplianceValidator _protocolValidator;
    private readonly SecurityValidator _securityValidator;

    public CalibrationTargetIntegrationTests(McpServerTestFixture fixture)
    {
        _fixture = fixture;
        var ruleRegistry = new ProtocolRuleRegistry();
        var applicabilityResolver = new ValidationApplicabilityResolver(new EmbeddedSchemaRegistry());
        var featureResolver = new ProtocolFeatureResolver(
            new ValidationPackRegistry<IProtocolFeaturePack>(
                [new BuiltInProtocolFeaturePack(new EmbeddedSchemaRegistry())]));
        _protocolValidator = new ProtocolComplianceValidator(
            Mock.Of<ILogger<ProtocolComplianceValidator>>(),
            fixture.McpClient,
            ruleRegistry,
            applicabilityResolver,
            featureResolver);
        var loggerFactory = LoggerFactory.Create(_ => { });
        _securityValidator = new SecurityValidator(
            loggerFactory.CreateLogger<SecurityValidator>(),
            loggerFactory,
            fixture.McpClient,
            new McpCompliantAuthValidator(
                loggerFactory.CreateLogger<McpCompliantAuthValidator>(),
                fixture.McpClient));
    }

    [Fact]
    public async Task RunnableHttpTargets_ShouldMatchCorpusVerdictsAndScoreRanges()
    {
        var observedScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var targetKind in new[] { "compliant", "vulnerable", "malformed", "unavailable", "throttled", "auth-protected" })
        {
            _fixture.ResetMockServer();
            if (targetKind == "vulnerable")
            {
                ConfigureProtocolCompliantTarget(requireAuthentication: false);
            }
            else
            {
                ConfigureTarget(targetKind);
            }
            var serverConfig = _fixture.CreateTestServerConfig();

            var protocolResult = await _protocolValidator.ValidateJsonRpcComplianceAsync(
                serverConfig,
                new ProtocolComplianceConfig { TestJsonRpcCompliance = true, ProtocolVersion = "2024-11-05" });
            if (targetKind == "vulnerable")
            {
                _fixture.ResetMockServer();
                ConfigureTarget(targetKind);
            }
            var securityResult = await _securityValidator.PerformSecurityAssessmentAsync(
                serverConfig,
                new SecurityTestingConfig
                {
                    TestInputValidation = false,
                    TestInjectionAttacks = false,
                    TestAuthenticationBypass = true
                });
            var observation = await ObserveTargetAsync(targetKind, serverConfig.Endpoint!);
            var result = BuildValidationResult(targetKind, observation, protocolResult, securityResult);
            var expected = ReadExpected(targetKind);
            var verdict = ValidationVerdictEngine.Calculate(result);
            var score = new SecurityFocusedScoringStrategy().CalculateScore(result);

            var evidence = string.Join(" | ", verdict.TriggeredDecisions.Select(decision => $"{decision.RuleId}:{decision.Summary}"));
            verdict.BaselineVerdict.Should().Be(expected.Baseline,
                $"{targetKind}; security={securityResult.Status}/{securityResult.SecurityScore}; vulnerabilities={securityResult.Vulnerabilities.Count}; decisions={evidence}");
            verdict.ProtocolVerdict.Should().Be(expected.Protocol, $"{targetKind}; decisions={evidence}");
            verdict.CoverageVerdict.Should().Be(expected.Coverage, targetKind);
            score.OverallScore.Should().Be(expected.Score, targetKind);
            observedScores[targetKind] = score.OverallScore;
        }

        observedScores["compliant"].Should().BeGreaterThan(observedScores["malformed"]);
        observedScores["malformed"].Should().BeGreaterThan(observedScores["vulnerable"]);
        observedScores["unavailable"].Should().BeLessThanOrEqualTo(ScoringConstants.LowCoverageScoreCap);
        observedScores["throttled"].Should().BeLessThanOrEqualTo(ScoringConstants.LowCoverageScoreCap);
        observedScores["auth-protected"].Should().BeLessThanOrEqualTo(ScoringConstants.LowCoverageScoreCap);
    }

    private async Task<TargetObservation> ObserveTargetAsync(string targetKind, string endpoint)
    {
        var baseline = await _fixture.McpClient.CallAsync(endpoint, "tools/list", new { }, CancellationToken.None);
        JsonRpcResponse? credentialVariant = null;
        if (baseline.StatusCode == 401)
        {
            credentialVariant = await _fixture.McpClient.CallAsync(
                endpoint,
                "tools/list",
                new { },
                new AuthenticationConfig { Type = "Bearer", Token = "invalid-calibration-token" },
                CancellationToken.None);
        }

        return new TargetObservation(baseline, credentialVariant);
    }

    private void ConfigureTarget(string targetKind)
    {
        if (targetKind == "vulnerable")
        {
            ConfigureProtocolCompliantTarget(requireAuthentication: true);
            _fixture.MockServer
                .Given(Request.Create()
                    .WithPath("/mcp")
                    .WithHeader("Authorization", new RegexMatcher("^Bearer .+", ignoreCase: true)))
                .RespondWith(JsonRpcSuccess());
            _fixture.MockServer
                .Given(Request.Create().WithPath("/mcp"))
                .RespondWith(Response.Create()
                    .WithStatusCode(401)
                    .WithHeader("WWW-Authenticate", "Bearer realm=\"calibration\""));
            return;
        }

        var response = targetKind switch
        {
            "compliant" => null,
            "malformed" => Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{ malformed json"),
            "unavailable" => Response.Create().WithStatusCode(503),
            "throttled" => Response.Create().WithStatusCode(429).WithHeader("Retry-After", "0"),
            "auth-protected" => Response.Create()
                .WithStatusCode(401)
                .WithHeader("WWW-Authenticate", "Bearer realm=\"calibration\""),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null)
        };
        if (targetKind == "compliant")
        {
            ConfigureProtocolCompliantTarget(requireAuthentication: false);
            return;
        }

        _fixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(response!);
    }

    private void ConfigureProtocolCompliantTarget(bool requireAuthentication)
    {
        RegisterProtocolResponse(@"^\{\s*invalid", JsonRpcError(-32700), requireAuthentication);
        RegisterProtocolResponse(@"^\{\s*""method""", JsonRpcError(-32600), requireAuthentication);
        RegisterProtocolResponse(@"""JsonRpc""\s*:", JsonRpcError(-32600), requireAuthentication);
        RegisterProtocolResponse(@"""method""\s*:\s*""nonexistent_method_12345""", JsonRpcError(-32601), requireAuthentication);
        RegisterProtocolResponse(@"""method""\s*:\s*""tools/call"".*invalid_params_string", JsonRpcError(-32602), requireAuthentication);
        RegisterProtocolResponse(@"""method""\s*:\s*""tools/call"".*""arguments""\s*:\s*\[", JsonRpcError(-32602), requireAuthentication);
        RegisterProtocolResponse(@"""method""\s*:\s*""notifications/initialized""", Response.Create().WithStatusCode(202), requireAuthentication);

        var contentTypeRequest = Request.Create()
            .WithPath("/mcp")
            .WithHeader("Content-Type", new RegexMatcher("^text/plain", ignoreCase: true));
        if (requireAuthentication)
        {
            contentTypeRequest.WithHeader("Authorization", "Bearer invalid-calibration-token");
        }
        _fixture.MockServer.Given(contentTypeRequest).RespondWith(Response.Create().WithStatusCode(415));

        var successRequest = Request.Create().WithPath("/mcp");
        if (requireAuthentication)
        {
            successRequest.WithHeader("Authorization", "Bearer invalid-calibration-token");
        }
        _fixture.MockServer.Given(successRequest).RespondWith(JsonRpcSuccess());
    }

    private void RegisterProtocolResponse(string bodyPattern, IResponseBuilder response, bool requireAuthentication)
    {
        var request = Request.Create()
            .WithPath("/mcp")
            .WithBody(new RegexMatcher(bodyPattern));
        if (requireAuthentication)
        {
            request.WithHeader("Authorization", "Bearer invalid-calibration-token");
        }
        _fixture.MockServer.Given(request).RespondWith(response);
    }

    private static IResponseBuilder JsonRpcError(int code) => Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBody($"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":{code},\"message\":\"Calibration error\"}},\"id\":\"calibration\"}}");

    private static IResponseBuilder JsonRpcSuccess() => Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBody("{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[]},\"id\":\"calibration\"}");

    private static ValidationResult BuildValidationResult(
        string targetKind,
        TargetObservation observation,
        ComplianceTestResult protocolResult,
        SecurityTestResult securityResult)
    {
        var baselineJsonValid = IsJsonRpcSuccess(observation.Baseline);
        var authBypassObserved = observation.Baseline.StatusCode == 401 && observation.CredentialVariant?.IsSuccess == true;
        var outcome = observation.Baseline.StatusCode switch
        {
            200 when baselineJsonValid => ValidationOutcome.Succeeded,
            200 => ValidationOutcome.Failed,
            401 when authBypassObserved => ValidationOutcome.Failed,
            401 => ValidationOutcome.AuthRequired,
            429 => ValidationOutcome.Inconclusive,
            >= 500 => ValidationOutcome.Unavailable,
            _ => ValidationOutcome.Error
        };
        var protocolOutcome = authBypassObserved ? ValidationOutcome.Succeeded : outcome;
        var result = new ValidationResult
        {
            ProtocolCompliance = protocolResult,
            SecurityTesting = securityResult
        };
        result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
            "protocol-core",
            targetKind,
            protocolOutcome,
            protocolResult.Message));
        return result;
    }

    private static bool IsJsonRpcSuccess(JsonRpcResponse response)
    {
        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(response.RawJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("jsonrpc", out var jsonRpc) &&
                   jsonRpc.GetString() == "2.0" &&
                   root.TryGetProperty("result", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ExpectedCalibration ReadExpected(string targetKind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Calibration", "corpus-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var element = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == targetKind);
        return new ExpectedCalibration(
            Enum.Parse<ValidationVerdict>(element.GetProperty("expectedBaseline").GetString()!),
            Enum.Parse<ValidationVerdict>(element.GetProperty("expectedProtocol").GetString()!),
            Enum.Parse<ValidationVerdict>(element.GetProperty("expectedCoverage").GetString()!),
            element.GetProperty("expectedScore").GetDouble());
    }

    private sealed record TargetObservation(JsonRpcResponse Baseline, JsonRpcResponse? CredentialVariant);

    private sealed record ExpectedCalibration(
        ValidationVerdict Baseline,
        ValidationVerdict Protocol,
        ValidationVerdict Coverage,
        double Score);
}
