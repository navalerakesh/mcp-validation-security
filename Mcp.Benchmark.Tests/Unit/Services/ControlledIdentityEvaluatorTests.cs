using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ControlledIdentityEvaluatorTests
{
    [Theory]
    [InlineData("maximum", true)]
    [InlineData("too-many", false)]
    [InlineData("empty", false)]
    [InlineData("invalid-policy", false)]
    public void SignedIdentityAdmission_ShouldEnforceDeclaredMatrixBeforeAcceptingSignedEvidence(string variant, bool admitted)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var count = variant == "too-many" ? ControlledIdentityMatrixContract.MaximumCases + 1 : ControlledIdentityMatrixContract.MaximumCases;
        var entries = Enumerable.Range(1, count - 1).Select(index => Probe() with
        {
            CaseId = $"probe-{index}", Binding = Probe().Binding with
            { OperationId = $"operation-{index}", AttemptId = $"attempt-{index}", RequestId = $"request-{index}" }
        }).Prepend(Control()).ToArray();
        var contract = Contract() with { Cases = variant == "empty" ? [] : entries };
        if (variant == "invalid-policy") contract = contract with { PolicyRevision = "" };
        var document = SignedDocument(key, false) with { Contract = contract, Observations = contract.Cases.Select(Observation).ToArray() };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var verify = () => ControlledIdentityEvidenceVerifier.Verify(SerializeDocument(document), contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        if (admitted)
        {
            var assessment = verify();
            assessment.Outcome.Should().Be(ValidationOutcome.Succeeded);
            assessment.Cases.Should().HaveCount(ControlledIdentityMatrixContract.MaximumCases);
            assessment.RecordedObservations.Should().HaveCount(ControlledIdentityMatrixContract.MaximumCases);
        }
        else verify.Should().Throw<InvalidDataException>().Which.Message.Should().Be("Identity matrix admission is invalid.");
    }

    [Fact]
    public void IdentityAdmission_ShouldRejectNullRequiredInputsBeforeReadingEvidence()
    {
        var contract = () => ControlledIdentityEvidenceVerifier.Verify("{}", null!, Admission(), "untrusted", Clock);
        var admission = () => ControlledIdentityEvidenceVerifier.Verify("{}", Contract(), null!, "untrusted", Clock);
        var requirement = () => ControlledIdentitySecurityBaseline.VerifyRequirement(null!, Binding.RunId, Binding.TargetIdentitySha256, Clock);
        var projection = () => ControlledIdentitySecurityBaseline.Project(null!, null);
        contract.Should().Throw<ArgumentNullException>();
        admission.Should().Throw<ArgumentNullException>();
        requirement.Should().Throw<ArgumentNullException>();
        projection.Should().Throw<ArgumentNullException>();
    }

    private static readonly DateTimeOffset Clock = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly ControlledOperationBinding Binding = new("run-1", "operation-control", "attempt-control", "request-control", "boundary-1",
        new string('a', 64), new string('b', 64), new string('c', 64), "actor-a", "object-a");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignedIdentity_ShouldRejectMismatchedSupportedRevisions(bool legacyEnvelope)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = SignedDocument(key, false);
        var contract = Contract() with { ContractRevision = legacyEnvelope ? Contract().ContractRevision : ControlledIdentityMatrixContract.LegacyRevision };
        var document = original with { Contract = contract, DocumentSchemaVersion = legacyEnvelope ? ControlledIdentityEvidenceDocument.LegacyRevision : original.DocumentSchemaVersion };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var import = () => new ControlledIdentityEvidenceImporter().Import(SerializeDocument(document), contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        import.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("default-issued", false)]
    [InlineData("exact-expiry", false)]
    [InlineData("before-expiry", true)]
    [InlineData("observation-at-issuance", true)]
    [InlineData("observation-after-issuance", false)]
    public void SignedIdentity_ShouldRespectExactTimeBoundaries(string variant, bool valid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false);
        var assessedAt = Clock;
        if (variant == "default-issued") document = document with { IssuedAt = default, Observations = [] };
        if (variant is "exact-expiry" or "before-expiry") document = document with
        { IssuedAt = Clock.AddTicks(-1), ExpiresAt = variant == "exact-expiry" ? Clock : Clock.AddTicks(1) };
        if (variant.StartsWith("observation-", StringComparison.Ordinal))
        {
            document = document with { Observations = [Observation(Control()), Observation(Probe()) with
            { ObservedAt = variant == "observation-at-issuance" ? Clock : Clock.AddTicks(1) }] };
            assessedAt = Clock.AddTicks(2);
        }
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var import = () => new ControlledIdentityEvidenceImporter().Import(SerializeDocument(document), document.Contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), assessedAt);
        if (valid) import.Should().NotThrow().Which.Outcome.Should().Be(ValidationOutcome.Succeeded);
        else import.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("contract", "toolName")]
    [InlineData("observations", "observerId")]
    public void SignedIdentity_ShouldRejectIdenticalDuplicateFieldsWithinObjectsAndArrays(string container, string field)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false);
        var json = SerializeDocument(document);
        new ControlledIdentityEvidenceImporter().Import(json, document.Contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock)
            .Outcome.Should().Be(ValidationOutcome.Succeeded);
        using var parsed = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(parsed.RootElement, null, writer);
        var duplicated = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        var import = () => new ControlledIdentityEvidenceImporter().Import(duplicated, document.Contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        import.Should().Throw<InvalidDataException>();

        void Write(JsonElement element, string? parent, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, property.Name, writer);
                    if (parent == container && property.Name == field) property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var entry in element.EnumerateArray()) Write(entry, parent, writer);
                writer.WriteEndArray();
            }
            else element.WriteTo(writer);
        }
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("one-over", false)]
    [InlineData("utf8-over", false)]
    public void SignedReader_ShouldEnforceBytesIndependentlyOfCharacters(string variant, bool valid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false);
        if (variant == "utf8-over") document = document with
        { Contract = document.Contract with { ToolName = new string('\u6e2c', ExecutionPolicyDefaults.DefaultMaxResponseBytes / 3 + 1) } };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var json = JsonSerializer.Serialize(document, options);
        if (variant == "utf8-over")
        {
            json.Length.Should().BeLessThan(ExecutionPolicyDefaults.DefaultMaxResponseBytes);
            System.Text.Encoding.UTF8.GetByteCount(json).Should().BeGreaterThan(ExecutionPolicyDefaults.DefaultMaxResponseBytes);
        }
        else json += new string(' ', ExecutionPolicyDefaults.DefaultMaxResponseBytes - System.Text.Encoding.UTF8.GetByteCount(json) + (valid ? 0 : 1));
        var read = () => SignedControlledEvidenceReader.Read<ControlledIdentityEvidenceDocument>(json, key.ExportSubjectPublicKeyInfoPem(),
            document.DocumentType, document.DocumentSchemaVersion, Clock, _ => true, CancellationToken.None);
        if (valid) read.Should().NotThrow();
        else read.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("actor-role")]
    [InlineData("actor-expected")]
    [InlineData("actor-binding")]
    [InlineData("actor-object")]
    [InlineData("actor-arguments")]
    [InlineData("scope-expected")]
    [InlineData("scope-binding")]
    [InlineData("scope-object")]
    [InlineData("scope-arguments")]
    [InlineData("authorization-unchanged")]
    public void SecurityBaseline_ShouldRequireEveryDeclaredChallengeRelationship(string variation)
    {
        var caseId = variation.StartsWith("actor", StringComparison.Ordinal) ? "actor-probe" :
            variation.StartsWith("scope", StringComparison.Ordinal) ? "scope-probe" : "authorization-probe";
        var result = new ValidationResult();
        AddSecurityBaseline(result, transformCase: entry => entry.CaseId != caseId ? entry : variation switch
        {
            "actor-role" => entry with { IdentityRole = ControlledIdentityRole.ResourceSelector },
            "actor-expected" or "scope-expected" => entry with { ExpectedEffectiveActorAlias = "actor-b" },
            "actor-binding" or "scope-binding" => entry with { Binding = entry.Binding with { ActorAlias = "actor-b" } },
            "actor-object" or "scope-object" => entry with { Binding = entry.Binding with { ObjectAlias = "object-b" } },
            "actor-arguments" => entry with { Binding = entry.Binding with { ArgumentsSha256 = Control().Binding.ArgumentsSha256 } },
            "scope-arguments" => entry with { Binding = entry.Binding with { ArgumentsSha256 = new string('f', 64) } },
            _ => entry with { Binding = entry.Binding with { ActorAlias = Control().Binding.ActorAlias, ObjectAlias = Control().Binding.ObjectAlias } }
        });
        var requirement = result.Assurance.RequiredIdentityControls!;
        ControlledIdentitySecurityBaseline.HasCompleteSelection(requirement).Should().BeFalse();
        var claimId = caseId == "actor-probe" ? "security-baseline:actor-binding" : caseId == "scope-probe"
            ? "security-baseline:scope-enforcement" : "security-baseline:actor-action-object-authorization";
        var claims = ControlledIdentitySecurityBaseline.Project(result, requirement);
        claims.Single(claim => claim.ClaimId == claimId).Outcome.Should().Be(AssuranceClaimOutcome.Incomplete);
        claims.Where(claim => claim.ClaimId != claimId).Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("authentication")]
    [InlineData("scope")]
    public void SecurityBaseline_OneUnknownPredicate_ShouldNotEarnCompleteCredit(string predicate)
    {
        var result = new ValidationResult();
        var caseId = predicate == "scope" ? "scope-probe" : "actor-probe";
        AddSecurityBaseline(result, transformObservation: observation => observation.CaseId != caseId ? observation : predicate switch
        {
            "actor" => observation with { EffectiveActorMatched = null },
            "authentication" => observation with { AuthenticationBindingMatched = null },
            _ => observation with { ScopeMatched = null }
        });
        var claimId = predicate == "scope" ? "security-baseline:scope-enforcement" : "security-baseline:actor-binding";
        var claims = ControlledIdentitySecurityBaseline.Project(result, result.Assurance.RequiredIdentityControls);
        var incomplete = claims.Single(claim => claim.ClaimId == claimId);
        incomplete.Outcome.Should().Be(AssuranceClaimOutcome.Incomplete);
        incomplete.Confidence.Should().Be(EvidenceConfidenceLevel.None);
        claims.Where(claim => claim.ClaimId != claimId).Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("positive")]
    public void SecurityBaseline_SelectionGuard_ShouldRejectIncompleteOrReusedCases(string variation)
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result);
        var requirement = result.Assurance.RequiredIdentityControls!;
        ControlledIdentitySecurityBaseline.HasCompleteSelection(requirement).Should().BeTrue();
        requirement = requirement with { ActorBindingCaseId = variation switch
        { "duplicate" => requirement.ScopeEnforcementCaseId, "missing" => "absent", "positive" => "control", _ => " " } };
        ControlledIdentitySecurityBaseline.HasCompleteSelection(requirement).Should().BeFalse();
        if (variation == "duplicate") ControlledIdentitySecurityBaseline.Project(result, requirement)
            .Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("admission")]
    [InlineData("json")]
    [InlineData("key")]
    [InlineData("key-tail")]
    public void SecurityBaseline_DirectAdmission_ShouldRejectMissingFieldsWithoutEcho(string variation)
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result);
        var requirement = result.Assurance.RequiredIdentityControls!;
        requirement = variation switch
        {
            "contract" => requirement with { Contract = null! }, "admission" => requirement with { Admission = null! },
            "json" => requirement with { SignedEvidenceJson = null! }, "key" => requirement with { TrustedObserverPublicKeyPem = null! },
            _ => requirement with { TrustedObserverPublicKeyPem = requirement.TrustedObserverPublicKeyPem + "untrusted-canary" }
        };
        var verify = () => ControlledIdentitySecurityBaseline.VerifyRequirement(requirement, Binding.RunId, Binding.TargetIdentitySha256, Clock);
        verify.Should().Throw<InvalidDataException>().Which.Message.Should().NotBeNullOrWhiteSpace().And.NotContain("untrusted-canary");
    }

    [Fact]
    public void LegacySignedIdentity_ShouldRemainVerifiableWithoutNewChallengeClaims()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract() with { ContractRevision = ControlledIdentityMatrixContract.LegacyRevision };
        var document = SignedDocument(key, false) with { DocumentSchemaVersion = ControlledIdentityEvidenceDocument.LegacyRevision, Contract = contract };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var assessment = new ControlledIdentityEvidenceImporter().Import(SerializeDocument(document), contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);

        assessment.Outcome.Should().Be(ValidationOutcome.Succeeded);
        assessment.AdmittedContract!.Cases.Should().OnlyContain(entry => entry.SecurityChallenge == null);
        Evaluate(contract with { Cases = [Control(), Probe() with { SecurityChallenge = ControlledIdentityChallenge.ActorSubstitution }] }, [])
            .Outcome.Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData("actor-probe")]
    [InlineData("scope-probe")]
    public void SecurityBaseline_AllowedNegativeChallenge_ShouldRemainViolated(string caseId)
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result, allowedChallenge: caseId);
        var controlId = caseId == "actor-probe" ? "actor-binding" : "scope-enforcement";

        result.Assurance.Claims.Single(claim => claim.Subject == controlId && claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
            .Outcome.Should().Be(AssuranceClaimOutcome.Violated);
    }

    [Fact]
    public void SecurityBaseline_QuarantinedRawCanary_ShouldNotBeRetainedInPublicResult()
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result);
        var requirement = result.Assurance.RequiredIdentityControls!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var document = JsonSerializer.Deserialize<ControlledIdentityEvidenceDocument>(requirement.SignedEvidenceJson, options)!;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string canary = "private-observer-canary";
        document = document with
        {
            SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()),
            Observations = document.Observations.Select(observation => observation.CaseId == "actor-probe"
                ? observation with { ObserverId = canary } : observation).ToArray()
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var json = SerializeDocument(document);
        var assessment = new ControlledIdentityEvidenceImporter().Import(json, requirement.Contract, requirement.Admission, key.ExportSubjectPublicKeyInfoPem(), Clock);
        assessment.RecordedGaps.Should().NotBeEmpty();
        result.Assessments.Scenarios.Clear();
        result.Assessments.Scenarios.Add(new ValidationScenarioResult { ScenarioId = "quarantined", DisplayName = "Quarantined", ControlledIdentity = assessment });
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredIdentityControls = requirement with { SignedEvidenceJson = json, TrustedObserverPublicKeyPem = key.ExportSubjectPublicKeyInfoPem() } });

        result.Assurance.RequiredIdentityControls.Should().BeNull();
        JsonSerializer.Serialize(result.CloneWithoutSecrets()).Should().NotContain(canary);
    }

    [Fact]
    public void BaselineRunRequest_ShouldFreezeSelectedCasesAndNotSerializeEvidence()
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result);
        var requirement = result.Assurance.RequiredIdentityControls!;
        var cases = requirement.Contract.Cases.ToList();
        var request = ValidationRunRequest.Capture(new McpValidatorConfiguration(), Binding.RunId)
            .WithIdentityControls(requirement with { Contract = requirement.Contract with { Cases = cases } });
        cases.Clear();

        request.IdentityControls!.Contract.Cases.Should().HaveCount(4);
        request.RequestId.Should().Be(Binding.RunId);
        JsonSerializer.Serialize(request).Should().NotContain("SignedEvidenceJson").And.NotContain("TrustedObserverPublicKeyPem");
    }

    [Fact]
    public void SecurityBaseline_UnsignedObservationMutation_ShouldNotEraseSignedFailure()
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result, actorViolation: true);
        var assessment = result.Assessments.Scenarios.Single().ControlledIdentity!;
        result.Assessments.Scenarios[0] = new ValidationScenarioResult
        {
            ScenarioId = "selected-signed-matrix", DisplayName = "Selected signed matrix",
            ControlledIdentity = assessment with
            {
                RecordedObservations = assessment.RecordedObservations!.Select(observation => observation with { EffectiveActorMatched = true }).ToArray()
            }
        };

        foreach (var candidate in new[] { result, result.CloneWithoutSecrets() })
        {
            var evaluation = AssuranceAssessmentEngine.Evaluate(candidate, AssuranceProfileCatalog.Comprehensive);
            evaluation.Claims.Single(claim => claim.ClaimId == "security-baseline:actor-binding").Outcome.Should().Be(AssuranceClaimOutcome.Violated);
            evaluation.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
                .Should().NotContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
        }
    }

    [Fact]
    public void SecurityBaseline_TamperedSignedEnvelope_ShouldNotPersistOrEarnCredit()
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result, actorViolation: true);
        var requirement = result.Assurance.RequiredIdentityControls!;
        var json = System.Text.Json.Nodes.JsonNode.Parse(requirement.SignedEvidenceJson)!;
        json["observations"]!.AsArray().Single(observation => observation!["caseId"]!.GetValue<string>() == "actor-probe")!["effectiveActorMatched"] = true;
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredIdentityControls = requirement with { SignedEvidenceJson = json.ToJsonString() } });

        result.Assurance.RequiredIdentityControls.Should().BeNull();
        result.Assurance.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
            .Should().HaveCount(3).And.OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete);
    }

    [Fact]
    public void SecurityBaseline_ObjectOnlyMatrix_ShouldNotCreditUnexercisedControls()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var probe = Probe() with { SecurityChallenge = ControlledIdentityChallenge.UnauthorizedActorOrObject };
        var contract = Contract() with { Cases = [Control(), probe] };
        var document = SignedDocument(key, false) with { Contract = contract, Observations = [Observation(Control()), Observation(probe)] };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var json = SerializeDocument(document);
        var assessment = new ControlledIdentityEvidenceImporter().Import(json, contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        var result = new ValidationResult { ValidationId = Binding.RunId, StartTime = Clock.UtcDateTime, EndTime = Clock.UtcDateTime };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        result.Assessments.Scenarios.Add(new ValidationScenarioResult { ScenarioId = "object-only", DisplayName = "Object only", ControlledIdentity = assessment });
        var claims = ControlledIdentitySecurityBaseline.Project(result, new ControlledIdentitySecurityRequirement(contract, Admission(), json,
            key.ExportSubjectPublicKeyInfoPem(), "actor-probe", "scope-probe", "probe"));

        claims.Single(claim => claim.Subject == "actor-binding").Outcome.Should().Be(AssuranceClaimOutcome.Incomplete);
        claims.Single(claim => claim.Subject == "scope-enforcement").Outcome.Should().Be(AssuranceClaimOutcome.Incomplete);
        claims.Single(claim => claim.Subject == "actor-action-object-authorization").Outcome.Should().Be(AssuranceClaimOutcome.Satisfied);
    }

    [Theory]
    [InlineData("actor-binding")]
    [InlineData("scope-enforcement")]
    [InlineData("actor-action-object-authorization")]
    public void SecurityBaseline_MissingSelectedChallenge_ShouldOnlyRemoveItsCredit(string control)
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result);
        var requirement = result.Assurance.RequiredIdentityControls!;
        requirement = control switch
        {
            "actor-binding" => requirement with { ActorBindingCaseId = "absent" },
            "scope-enforcement" => requirement with { ScopeEnforcementCaseId = "absent" },
            _ => requirement with { AuthorizationCaseId = "absent" }
        };
        var claims = ControlledIdentitySecurityBaseline.Project(result, requirement);

        claims.Single(claim => claim.Subject == control).Outcome.Should().Be(AssuranceClaimOutcome.Incomplete);
        claims.Where(claim => claim.Subject != control).Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
    }

    [Theory]
    [InlineData("complete", AssuranceClaimOutcome.Satisfied)]
    [InlineData("missing", AssuranceClaimOutcome.Incomplete)]
    [InlineData("wrong-key", AssuranceClaimOutcome.Incomplete)]
    [InlineData("wrong-target", AssuranceClaimOutcome.Incomplete)]
    [InlineData("stale", AssuranceClaimOutcome.Incomplete)]
    [InlineData("failed-actor", AssuranceClaimOutcome.Violated)]
    public void SecurityBaseline_ShouldRequireSelectedBoundEvidence(string variant, AssuranceClaimOutcome actorOutcome)
    {
        var result = new ValidationResult();
        AddSecurityBaseline(result, variant == "failed-actor");
        var requirement = variant == "missing" ? null : result.Assurance.RequiredIdentityControls;
        if (variant == "wrong-target") result.Run.TargetIdentitySha256 = new string('f', 64);
        if (variant == "stale") result.EndTime = Clock.AddDays(1).UtcDateTime;
        if (variant == "wrong-key")
        {
            using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            requirement = requirement! with { TrustedObserverPublicKeyPem = wrongKey.ExportSubjectPublicKeyInfoPem() };
        }

        var claims = ControlledIdentitySecurityBaseline.Project(result, requirement);

        claims.Should().HaveCount(3);
        claims.Should().OnlyContain(claim => claim.Dimension == AssuranceDimension.Security);
        claims.Single(claim => claim.Subject == "actor-binding").Outcome.Should().Be(actorOutcome);
        if (variant == "complete") claims.Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
        if (variant is "missing" or "wrong-key" or "wrong-target" or "stale")
            claims.Should().OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete);
        foreach (var claim in claims)
        {
            claim.Authority.Should().Be(AssuranceAuthority.DeterministicSecurity);
            claim.RequirementLevel.Should().Be(AssuranceRequirementLevel.SecurityControl);
            claim.Confidence.Should().Be(claim.Outcome == AssuranceClaimOutcome.Incomplete ? EvidenceConfidenceLevel.None : EvidenceConfidenceLevel.High);
            claim.Source.Revision.Should().Be(variant is "complete" or "failed-actor" ? "fixture-policy-v1" : AssuranceProfileCatalog.ComprehensiveRevision);
            claim.EvidenceIds.Should().Equal(variant is "complete" or "failed-actor" ? ["selected-signed-matrix"] : Array.Empty<string>());
            claim.Summary.Should().Contain(claim.Outcome == AssuranceClaimOutcome.Violated ? "failed" : claim.Outcome == AssuranceClaimOutcome.Satisfied ? "satisfied" : "lacks complete");
            claim.Remediation.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownViolations_ShouldSurviveMissingControlAndRetainEveryObligation(bool reverse)
    {
        var contract = Contract();
        if (reverse) contract = contract with { Cases = contract.Cases.Reverse().ToArray() };
        var result = Evaluate(contract, [Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Allowed, EffectiveActorMatched = false }]);

        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.Cases.Select(entry => entry.CaseId).Should().Equal("control", "probe");
        result.Cases[0].ObservationOutcome.Should().Be(ValidationOutcome.Unavailable);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.RetryPermitted.Should().BeFalse();
    }

    [Fact]
    public void ValidMatrix_ShouldBeOrderIndependentAndReplayable()
    {
        var observations = new[] { Observation(Control()), Observation(Probe()) };
        var first = Evaluate(Contract(), observations);
        var second = Evaluate(Contract() with { Cases = Contract().Cases.Reverse().ToArray() }, observations.Reverse().ToArray());
        var restored = JsonSerializer.Deserialize<ControlledIdentityMatrixAssessment>(JsonSerializer.Serialize(first))!;

        first.Outcome.Should().Be(ValidationOutcome.Succeeded);
        JsonSerializer.Serialize(second).Should().Be(JsonSerializer.Serialize(first));
        JsonSerializer.Serialize(Evaluate(restored.AdmittedContract!, restored.RecordedObservations!)).Should().Be(JsonSerializer.Serialize(first));
    }

    [Theory]
    [InlineData(ControlledIdentityRole.ResourceSelector, null, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.DisplayOnly, null, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.Unknown, null, ValidationOutcome.Inconclusive)]
    [InlineData(ControlledIdentityRole.DelegationReference, true, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.DelegationReference, false, ValidationOutcome.Failed)]
    [InlineData(ControlledIdentityRole.DelegationReference, null, ValidationOutcome.Inconclusive)]
    [InlineData(ControlledIdentityRole.ApproverAssertion, true, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.ApproverAssertion, false, ValidationOutcome.Failed)]
    public void Roles_ShouldUseExplicitBindingEvidenceRatherThanNames(ControlledIdentityRole role, bool? matched, ValidationOutcome expected)
    {
        var probe = Probe() with { IdentityRole = role, ExpectedDecision = ControlledAuthorizationDecision.Allowed, ExpectedEffectiveActorAlias = "delegate-b" };
        var result = Evaluate(Contract() with { Cases = [Control(), probe] }, [Observation(Control()), Observation(probe) with
        {
            EffectiveActorMatched = true, DelegationBindingMatched = matched, ApprovalBindingMatched = matched
        }]);

        result.Outcome.Should().Be(expected);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Succeeded);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("observer")]
    [InlineData("age")]
    [InlineData("delay")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("duplicate-case")]
    [InlineData("operation")]
    [InlineData("attempt")]
    [InlineData("request")]
    [InlineData("run")]
    [InlineData("target")]
    [InlineData("route")]
    [InlineData("digest")]
    [InlineData("alias")]
    [InlineData("role")]
    [InlineData("decision")]
    [InlineData("control-reference")]
    [InlineData("control-boundary")]
    [InlineData("control-predicate")]
    [InlineData("control-cycle")]
    public void MalformedPlans_ShouldBeBlockedWithoutRetainingUntrustedInput(string fault)
    {
        var probe = Probe();
        var contract = fault switch
        {
            "version" => Contract() with { ContractRevision = "future" },
            "policy" => Contract() with { PolicyRevision = " " },
            "observer" => Contract() with { ObserverId = "target-self-report" },
            "age" => Contract() with { MaximumEvidenceAge = TimeSpan.Zero },
            "delay" => Contract() with { MaximumObservationDelay = TimeSpan.Zero },
            "empty" => Contract() with { Cases = [] },
            "oversized" => Contract() with { Cases = Enumerable.Repeat(Control(), 65).ToArray() },
            _ => Contract()
        };
        probe = fault switch
        {
            "duplicate-case" => probe with { CaseId = "control" },
            "operation" => probe with { Binding = probe.Binding with { OperationId = Binding.OperationId } },
            "attempt" => probe with { Binding = probe.Binding with { AttemptId = Binding.AttemptId } },
            "request" => probe with { Binding = probe.Binding with { RequestId = Binding.RequestId } },
            "run" => probe with { Binding = probe.Binding with { RunId = "other-run" } },
            "target" => probe with { Binding = probe.Binding with { TargetIdentitySha256 = new string('d', 64) } },
            "route" => probe with { Binding = probe.Binding with { RouteIdentitySha256 = new string('d', 64) } },
            "digest" => probe with { Binding = probe.Binding with { ArgumentsSha256 = "invalid" } },
            "alias" => probe with { ExpectedEffectiveActorAlias = new string('x', 513) },
            "role" => probe with { IdentityRole = (ControlledIdentityRole)999 },
            "decision" => probe with { ExpectedDecision = ControlledAuthorizationDecision.Unknown },
            "control-reference" => probe with { PositiveControlCaseId = "absent-control" },
            "control-boundary" => probe with { Binding = probe.Binding with { BoundaryId = "another-boundary" } },
            "control-predicate" => probe with { ScopePredicateId = "another-policy" },
            "control-cycle" => probe with { PositiveControlCaseId = probe.CaseId },
            _ => probe
        };
        if (fault is not ("empty" or "oversized")) contract = contract with { Cases = [Control(), probe] };

        var result = Evaluate(contract, []);

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.AdmittedContract.Should().BeNull();
        result.RecordedObservations.Should().BeNull();
    }

    [Theory]
    [InlineData("case")]
    [InlineData("binding")]
    [InlineData("observer")]
    [InlineData("predicate")]
    [InlineData("decision")]
    [InlineData("outcome")]
    [InlineData("alias")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public void RejectedObservations_ShouldRetainAdmittedObligations(string fault)
    {
        var observation = fault switch
        {
            "case" => Observation(Probe()) with { CaseId = "unknown" },
            "binding" => Observation(Probe()) with { Binding = Probe().Binding with { RequestId = "replayed" } },
            "observer" => Observation(Probe()) with { ObserverId = "untrusted" },
            "predicate" => Observation(Probe()) with { ScopePredicateId = "other" },
            "decision" => Observation(Probe()) with { Decision = (ControlledAuthorizationDecision)999 },
            "outcome" => Observation(Probe()) with { ObserverOutcome = (ValidationOutcome)999 },
            "alias" => Observation(Probe()) with { ObserverId = "secret-canary" },
            _ => Observation(Probe())
        };
        var observations = fault == "oversized" ? Enumerable.Repeat(observation, 65).ToArray()
            : fault == "duplicate" ? new[] { observation, observation } : [observation];

        var result = Evaluate(Contract(), observations);

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases.Select(entry => entry.CaseId).Should().Equal("control", "probe");
        result.Cases.Should().NotContain(entry => entry.Outcome == ValidationOutcome.Succeeded);
        result.EvidenceIntegrityOutcome.Should().Be(ValidationOutcome.Blocked);
        JsonSerializer.Serialize(result).Should().NotContain("secret");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("incomplete")]
    [InlineData("preconditions")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("observation")]
    [InlineData("reversed")]
    [InlineData("early-observation")]
    [InlineData("future")]
    [InlineData("stale")]
    [InlineData("unknown-decision")]
    [InlineData("unknown-actor")]
    [InlineData("unknown-auth")]
    [InlineData("unknown-scope")]
    public void MissingEvidence_ShouldNotEarnCredit(string gap)
    {
        var observation = gap switch
        {
            "incomplete" => Observation(Probe()) with { Complete = false },
            "preconditions" => Observation(Probe()) with { PreconditionsSatisfied = null },
            "start" => Observation(Probe()) with { OperationStartedAt = null },
            "end" => Observation(Probe()) with { OperationEndedAt = null },
            "observation" => Observation(Probe()) with { ObservedAt = null },
            "reversed" => Observation(Probe()) with { OperationEndedAt = Clock.AddMinutes(-1) },
            "early-observation" => Observation(Probe()) with { ObservedAt = Clock.AddSeconds(-4) },
            "future" => Observation(Probe()) with { ObservedAt = Clock.AddSeconds(1) },
            "stale" => Observation(Probe()) with { OperationStartedAt = Clock.AddMinutes(-3), OperationEndedAt = Clock.AddMinutes(-2), ObservedAt = Clock.AddSeconds(-61) },
            "unknown-decision" => Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Unknown },
            "unknown-actor" => Observation(Probe()) with { EffectiveActorMatched = null },
            "unknown-auth" => Observation(Probe()) with { AuthenticationBindingMatched = null },
            "unknown-scope" => Observation(Probe()) with { ScopeMatched = null },
            _ => Observation(Probe())
        };
        var result = Evaluate(Contract(), gap == "missing" ? [Observation(Control())] : [Observation(Control()), observation]);

        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases.Should().HaveCount(2);
        result.Cases[1].Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(ValidationOutcome.Unavailable)]
    [InlineData(ValidationOutcome.AuthRequired)]
    [InlineData(ValidationOutcome.Blocked)]
    [InlineData(ValidationOutcome.Cancelled)]
    [InlineData(ValidationOutcome.Error)]
    public void ObserverGaps_ShouldRetainTheirDistinctOutcome(ValidationOutcome outcome)
    {
        var result = Evaluate(Contract(), [Observation(Control()), Observation(Probe()) with { ObserverOutcome = outcome }]);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases[1].ObservationOutcome.Should().Be(outcome);
    }

    [Fact]
    public void LaterPositiveControl_ShouldNotEstablishEarlierProbe()
    {
        var result = Evaluate(Contract(), [Observation(Control()) with { ObservedAt = Clock }, Observation(Probe())]);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedControl_ShouldNotEraseIndependentViolationsAndShouldReplay(bool reverse)
    {
        var observations = new[]
        {
            Observation(Control()) with { Decision = (ControlledAuthorizationDecision)999, ObserverId = "single-line-secret-canary" },
            Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Allowed, EffectiveActorMatched = false }
        };
        var result = Evaluate(Contract(), reverse ? observations.Reverse().ToArray() : observations);
        var restored = JsonSerializer.Deserialize<ControlledIdentityMatrixAssessment>(JsonSerializer.Serialize(result))!;

        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.EvidenceIntegrityOutcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases[0].ObservationOutcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Failed);
        result.RecordedGaps.Should().Equal(new ControlledIdentityEvidenceGap("control", 1));
        JsonSerializer.Serialize(result).Should().NotContain("single-line-secret-canary");
        var replay = ControlledIdentityEvaluator.Evaluate(restored.AdmittedContract!, restored.RecordedObservations!, Binding.RunId,
            Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1", restored.AssessedAt, restored.RecordedGaps);
        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(5, ValidationOutcome.Succeeded)]
    [InlineData(6, ValidationOutcome.Inconclusive)]
    [InlineData(86400, ValidationOutcome.Inconclusive)]
    public void RecentObservation_ShouldNotRefreshOldOperation(int delay, ValidationOutcome expected)
    {
        var control = Observation(Control());
        var ended = control.ObservedAt!.Value.AddSeconds(-delay);
        var result = Evaluate(Contract(), [control with { OperationStartedAt = ended.AddSeconds(-1), OperationEndedAt = ended }, Observation(Probe())]);

        result.Outcome.Should().Be(expected);
        result.Cases[0].Outcome.Should().Be(expected);
    }

    [Fact]
    public void DuplicateObservation_ShouldQuarantineBothCopiesRegardlessOfOrder()
    {
        var valid = Observation(Probe());
        var conflicting = valid with { EffectiveActorMatched = false };
        var first = Evaluate(Contract(), [Observation(Control()), valid, conflicting]);
        var second = Evaluate(Contract(), [conflicting, Observation(Control()), valid]);

        first.Outcome.Should().Be(ValidationOutcome.Blocked);
        first.RecordedGaps.Should().Equal(new ControlledIdentityEvidenceGap("probe", 2));
        first.RecordedObservations.Should().ContainSingle(entry => entry.CaseId == "control");
        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedIdentity_ShouldPreserveActualPolicyOutcome(bool violation)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, violation);
        var json = SerializeDocument(document);
        var result = new ControlledIdentityEvidenceImporter().Import(json, Contract(), Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);

        result.Outcome.Should().Be(violation ? ValidationOutcome.Failed : ValidationOutcome.Succeeded);
        result.SourceEvidenceSha256.Should().Be(CanonicalJsonDigest.ComputeText(json));
        result.VerifiedObserverKeyId.Should().Be(document.SigningKeyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedPack_ShouldRetainCaseCoverageWithoutCoreCredit(bool wrongRun)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pack = new ControlledIdentityEvidenceImporter().ImportPack(SerializeDocument(SignedDocument(key, true)), Contract(), Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        var result = new ValidationResult { ValidationId = wrongRun ? "other-run" : Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var before = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        var executed = await pack.GetScenarios().Single().ExecuteAsync(new ValidationScenarioContext
        {
            ValidationResult = result, ServerConfig = new(), ValidationConfiguration = new(),
            ApplicabilityContext = new ValidationApplicabilityContext { NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "Public" }
        }, CancellationToken.None);
        result.Assessments.Scenarios.Add(executed.Scenario);
        result.Evidence.Coverage.AddRange(executed.Coverage);
        var after = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        executed.Scenario.ControlledIdentity!.Outcome.Should().Be(wrongRun ? ValidationOutcome.Blocked : ValidationOutcome.Failed);
        executed.Coverage.Should().OnlyContain(entry => entry.Lane == EvaluationLane.ControlledApplication);
        after.Assessments.CoreConformance.Should().BeEquivalentTo(before.Assessments.CoreConformance);
        after.CertificateEligibility.Should().BeEquivalentTo(before.CertificateEligibility);
        if (!wrongRun) executed.Coverage.Should().HaveCount(2);
        result.Assurance = after;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var artifact = JsonSerializer.SerializeToNode(result.CloneWithoutSecrets(), options)!;
        var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "Schemas", "mcpval-validation-result.schema.json"));
        var validation = new Mcp.Benchmark.Infrastructure.Validators.JsonSchemaValidator().Validate(artifact,
            System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(schemaPath))!);
        validation.IsValid.Should().BeTrue(string.Join(Environment.NewLine, validation.Errors));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("tampered")]
    [InlineData("contract")]
    [InlineData("expired")]
    [InlineData("identity")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("oversized")]
    [InlineData("future-observation")]
    public void SignedIdentity_ShouldRejectUntrustedEnvelopeWithoutEcho(string fault)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false);
        if (fault == "future-observation")
        {
            document = document with { Observations = [Observation(Control()), Observation(Probe()) with { ObservedAt = Clock.AddSeconds(1) }] };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        }
        var node = System.Text.Json.Nodes.JsonNode.Parse(SerializeDocument(document))!;
        if (fault == "tampered") node["observations"]![1]!["effectiveActorMatched"] = false;
        if (fault == "expired") node["expiresAt"] = Clock.AddSeconds(-1);
        if (fault == "identity") node.AsObject().Remove("documentType");
        if (fault == "unknown-field") node["raw-identity"] = "secret-principal-canary";
        var contract = fault == "contract" ? Contract() with { PolicyRevision = "substituted" } : Contract();
        var json = node.ToJsonString();
        if (fault == "duplicate-field") json = "{\"documentType\":\"secret-principal-canary\"," + json[1..];
        if (fault == "oversized") json += new string(' ', ExecutionPolicyDefaults.DefaultMaxResponseBytes);
        var action = () => new ControlledIdentityEvidenceImporter().Import(json, contract, Admission(),
            (fault == "key" ? otherKey : key).ExportSubjectPublicKeyInfoPem(), Clock);

        action.Should().Throw<InvalidDataException>().Which.ToString().Should().NotContain("secret-principal-canary");
    }

    [Fact]
    public void CancelledImport_ShouldNotReadUntrustedEnvelope()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => new ControlledIdentityEvidenceImporter().Import("malformed", Contract(), Admission(), "not-a-key", Clock, cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(64, ValidationOutcome.Succeeded)]
    [InlineData(65, ValidationOutcome.Blocked)]
    public void MatrixCaseLimit_ShouldAdmitExactBoundary(int count, ValidationOutcome expected)
    {
        var cases = new List<ControlledIdentityCase> { Control() };
        for (var index = 1; index < count; index++) cases.Add(Probe() with
        {
            CaseId = $"probe-{index}", Binding = Probe().Binding with { OperationId = $"operation-{index}", AttemptId = $"attempt-{index}", RequestId = $"request-{index}" }
        });
        var result = Evaluate(Contract() with { Cases = cases }, cases.Select(Observation).ToArray());
        result.Outcome.Should().Be(expected);
        if (expected == ValidationOutcome.Succeeded) result.Cases.Should().HaveCount(count);
    }

    [Theory]
    [InlineData(512, ValidationOutcome.Succeeded)]
    [InlineData(513, ValidationOutcome.Blocked)]
    public void IdentifierLimit_ShouldAdmitExactBoundary(int length, ValidationOutcome expected)
    {
        var probe = Probe() with { CaseId = new string('x', length), ExpectedEffectiveActorAlias = new string('y', length) };
        Evaluate(Contract() with { Cases = [Control(), probe] }, [Observation(Control()), Observation(probe)]).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentAuthOrScopeMismatch_ShouldFailDespiteUnknownDecision(bool authentication)
    {
        var observed = Observation(Probe()) with
        {
            Decision = ControlledAuthorizationDecision.Unknown, AuthenticationBindingMatched = !authentication, ScopeMatched = authentication
        };
        var result = Evaluate(Contract(), [observed]);
        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    internal static void AddSecurityBaseline(ValidationResult result, bool actorViolation = false,
        string? runId = null, string? targetDigest = null, DateTimeOffset? assessedAt = null, string? allowedChallenge = null,
        Func<ControlledIdentityCase, ControlledIdentityCase>? transformCase = null,
        Func<ControlledIdentityObservation, ControlledIdentityObservation>? transformObservation = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clock = assessedAt ?? Clock;
        var offset = clock - Clock;
        var caseIds = new[] { "actor-probe", "scope-probe", "authorization-probe" };
        var probes = caseIds.Select(caseId => Probe() with
        {
            CaseId = caseId,
            IdentityRole = ControlledIdentityRole.CallerAssertion,
            SecurityChallenge = caseId switch
            {
                "actor-probe" => ControlledIdentityChallenge.ActorSubstitution,
                "scope-probe" => ControlledIdentityChallenge.InsufficientScope,
                _ => ControlledIdentityChallenge.UnauthorizedActorOrObject
            },
            Binding = Probe().Binding with
            {
                OperationId = $"operation-{caseId}", AttemptId = $"attempt-{caseId}", RequestId = $"request-{caseId}",
                ObjectAlias = caseId == "authorization-probe" ? Probe().Binding.ObjectAlias : Control().Binding.ObjectAlias,
                ArgumentsSha256 = caseId == "actor-probe" ? new string('f', 64) : Control().Binding.ArgumentsSha256
            }
        }).ToArray();
        var entries = probes.Prepend(Control()).Select(entry => entry with
        {
            Binding = entry.Binding with { RunId = runId ?? Binding.RunId, TargetIdentitySha256 = targetDigest ?? Binding.TargetIdentitySha256 }
        }).Select(entry => transformCase?.Invoke(entry) ?? entry).ToArray();
        var contract = Contract() with { Cases = entries };
        var admission = new ControlledIdentityAdmission(runId ?? Binding.RunId, targetDigest ?? Binding.TargetIdentitySha256,
            Binding.RouteIdentitySha256, contract.ObserverId);
        var original = SignedDocument(key, false);
        var document = original with
        {
            Contract = contract,
            IssuedAt = original.IssuedAt + offset, ExpiresAt = original.ExpiresAt + offset,
            Observations = entries.Select(entry =>
            {
                var observation = Observation(entry);
                return observation with
                {
                    EffectiveActorMatched = !(actorViolation && entry.CaseId == "actor-probe"),
                    Decision = entry.CaseId == allowedChallenge ? ControlledAuthorizationDecision.Allowed : observation.Decision,
                    OperationStartedAt = observation.OperationStartedAt + offset,
                    OperationEndedAt = observation.OperationEndedAt + offset,
                    ObservedAt = observation.ObservedAt + offset
                };
            }).Select(observation => transformObservation?.Invoke(observation) ?? observation).ToArray()
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var json = SerializeDocument(document);
        var assessment = new ControlledIdentityEvidenceImporter().Import(json,
            contract, admission, key.ExportSubjectPublicKeyInfoPem(), clock);
        result.ValidationId = admission.RunId;
        result.Run.TargetIdentitySha256 = admission.TargetIdentitySha256;
        result.StartTime = clock.UtcDateTime;
        result.EndTime = clock.UtcDateTime;
        result.Assessments.Scenarios.Add(new ValidationScenarioResult
        {
            ScenarioId = "selected-signed-matrix", DisplayName = "Selected signed matrix", ControlledIdentity = assessment
        });
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Comprehensive,
            new AssuranceEvaluationContext { RequiredIdentityControls = new ControlledIdentitySecurityRequirement(
                contract, admission, json, key.ExportSubjectPublicKeyInfoPem(), "actor-probe", "scope-probe", "authorization-probe") });
    }

    private static ControlledIdentityAdmission Admission() => new(Binding.RunId, Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1");

    [Theory]
    [InlineData("A", "a")]
    [InlineData("probe.a", "probe_a")]
    public async Task CaseScopes_ShouldSurviveProductionAggregationAndClaimNormalization(string controlId, string probeId)
    {
        var control = Control() with { CaseId = controlId };
        var probe = Probe() with { CaseId = probeId, PositiveControlCaseId = controlId };
        var contract = Contract() with { Cases = [control, probe] };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false) with
        {
            Contract = contract, Observations = [Observation(control), Observation(probe) with { EffectiveActorMatched = false }]
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var pack = new ControlledIdentityEvidenceImporter().ImportPack(SerializeDocument(document), contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        var result = new ValidationResult { ValidationId = Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var executed = await pack.GetScenarios().Single().ExecuteAsync(new ValidationScenarioContext
        {
            ValidationResult = result, ServerConfig = new(), ValidationConfiguration = new(),
            ApplicabilityContext = new ValidationApplicabilityContext { NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "Public" }
        }, CancellationToken.None);
        typeof(McpValidatorService).GetMethod("AddCoverageDeclarations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [result.Evidence.Coverage, executed.Coverage]);
        var claims = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory).Claims
            .Where(claim => claim.Dimension == AssuranceDimension.ControlledApplication).ToArray();

        result.Evidence.Coverage.Should().HaveCount(2);
        claims.Should().HaveCount(2);
        claims.Select(claim => claim.Outcome).Should().BeEquivalentTo([AssuranceClaimOutcome.Satisfied, AssuranceClaimOutcome.Violated]);
    }

    private static ControlledIdentityEvidenceDocument SignedDocument(ECDsa key, bool violation)
    {
        var document = new ControlledIdentityEvidenceDocument
        {
            Contract = Contract(), Observations = [Observation(Control()), Observation(Probe()) with { EffectiveActorMatched = !violation }],
            IssuedAt = Clock, ExpiresAt = Clock.AddMinutes(1), SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = string.Empty
        };
        return document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()),
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
    }

    private static string SerializeDocument(ControlledIdentityEvidenceDocument document)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(document, options);
    }

    private static ControlledIdentityCase Control() => new()
    {
        CaseId = "control", Binding = Binding, ScopePredicateId = "object-policy", ExpectedEffectiveActorAlias = "actor-a",
        ExpectedDecision = ControlledAuthorizationDecision.Allowed, IdentityRole = ControlledIdentityRole.CallerAssertion, IsPositiveControl = true
    };

    private static ControlledIdentityCase Probe() => Control() with
    {
        CaseId = "probe", Binding = Binding with { OperationId = "operation-probe", AttemptId = "attempt-probe", RequestId = "request-probe", ObjectAlias = "object-b" },
        ExpectedDecision = ControlledAuthorizationDecision.Denied, IsPositiveControl = false, PositiveControlCaseId = "control"
    };

    private static ControlledIdentityMatrixContract Contract() => new()
    {
        PolicyRevision = "fixture-policy-v1", ToolName = "owned-action", ObserverId = "observer-1", Cases = [Control(), Probe()],
        MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObservationDelay = TimeSpan.FromSeconds(5)
    };

    private static ControlledIdentityObservation Observation(ControlledIdentityCase entry) => new()
    {
        CaseId = entry.CaseId, Binding = entry.Binding, ObserverId = "observer-1", ScopePredicateId = entry.ScopePredicateId,
        OperationStartedAt = Clock.AddSeconds(entry.IsPositiveControl ? -20 : -5), OperationEndedAt = Clock.AddSeconds(entry.IsPositiveControl ? -15 : -3),
        ObservedAt = Clock.AddSeconds(entry.IsPositiveControl ? -10 : -1), Complete = true, PreconditionsSatisfied = true,
        ObserverOutcome = ValidationOutcome.Succeeded, Decision = entry.ExpectedDecision, EffectiveActorMatched = true,
        AuthenticationBindingMatched = true, ScopeMatched = true
    };

    private static ControlledIdentityMatrixAssessment Evaluate(ControlledIdentityMatrixContract contract, IReadOnlyList<ControlledIdentityObservation> observations) =>
        ControlledIdentityEvaluator.Evaluate(contract, observations, Binding.RunId, Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1", Clock);
}