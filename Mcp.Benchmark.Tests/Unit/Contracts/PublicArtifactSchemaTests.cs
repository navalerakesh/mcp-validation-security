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
    private readonly JsonSchemaValidator _validator = new();

    [Fact]
    public void ValidationResult_PublicJson_ConformsToPublishedSchema()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.PartiallyCompleted,
            ComplianceScore = 85
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
    }

    [Fact]
    public void AuditManifest_PublicJson_ConformsToPublishedSchema()
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

        AssertConforms("mcpval-audit-manifest.schema.json", Serialize(manifest));
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
            ["complianceScore"] = 90,
            ["trustLevel"] = "L3: Acceptable",
            ["trustLimitedByIncompleteEvidence"] = true,
            ["unevaluatedDimensions"] = new JsonArray("security", "operations"),
            ["overallStatus"] = "ReviewRequired",
            ["baselineVerdict"] = "ReviewRequired",
            ["protocolVerdict"] = "ReviewRequired",
            ["coverageVerdict"] = "ReviewRequired",
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
            BaselineVerdict = ValidationVerdict.Trusted
        };

        AssertConforms("mcpval-model-evaluation.schema.json", Serialize(artifact));
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
    public void ValidationResult_MalformedPolicyAndBaselineShapes_DoNotConformToPublishedSchema()
    {
        var result = new ValidationResult
        {
            PolicyOutcome = new ValidationPolicyOutcome
            {
                Mode = ValidationPolicyModes.Strict,
                Passed = false,
                Summary = "Blocked"
            },
            BaselineComparison = new ValidationBaselineComparison
            {
                BaselineValidationId = "baseline",
                BaselineDocumentSchemaVersion = "1.0.0"
            }
        };
        var instance = Serialize(result);
        instance["policyOutcome"]!["passed"] = "false";
        instance["baselineComparison"]!["baselineScore"] = 101;

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
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "1.0.0", true)]
    [InlineData(ArtifactContracts.ValidationResultDocumentType, "1.99.0", true)]
    [InlineData(ArtifactContracts.ModelEvaluationDocumentType, "1.0.0", true)]
    [InlineData(ArtifactContracts.ModelEvaluationDocumentType, "2.0.0", false)]
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
    public void ValidationResult_V1FixtureWithUnknownMinorFields_ShouldRemainConsumable()
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
        ArtifactContracts.IsCompatible(result.DocumentType, result.DocumentSchemaVersion).Should().BeTrue();
        AssertConforms("mcpval-validation-result.schema.json", JsonNode.Parse(json)!);
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
