using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Exceptions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class OfficialConformanceEvidenceImporter : IOfficialConformanceEvidenceImporter
{
    private const string LayerId = "official-conformance";

    public OfficialConformanceImportResult Import(string json, OfficialConformanceImportContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > OfficialConformanceContract.MaximumEvidenceBytes)
        {
            throw Invalid("Official conformance evidence exceeds the supported size limit.");
        }

        ValidateJsonShape(json);
        OfficialConformanceEvidenceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<OfficialConformanceEvidenceDocument>(
                json,
                OfficialConformanceContract.CreateJsonOptions())
                ?? throw Invalid("Official conformance evidence is empty.");
        }
        catch (OfficialConformanceEvidenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid("Official conformance evidence is not valid JSON for the supported contract.", exception);
        }

        ValidateDocument(document, context);
        var verified = OfficialConformanceContract.IsPinnedRunner(document.Runner) &&
            OfficialConformanceContract.IsPinnedRequirementSet(document.RequirementSet);
        var verificationStatus = verified && document.Run.State == OfficialConformanceRunState.Completed
            ? OfficialConformanceVerificationStatus.Verified
            : document.Run.State == OfficialConformanceRunState.Completed
                ? OfficialConformanceVerificationStatus.Unverified
                : OfficialConformanceVerificationStatus.Incomplete;
            var creditEligible = verified && document.Run.State == OfficialConformanceRunState.Completed;

        var coverage = new List<ValidationCoverageDeclaration>();
        var observations = new List<ValidationObservation>();
        var scenarios = document.Scenarios
            .OrderBy(scenario => scenario.ScenarioId, StringComparer.Ordinal)
            .Select(scenario => ProjectScenario(scenario, creditEligible, coverage, observations))
            .ToArray();
        var checks = scenarios.SelectMany(scenario => scenario.Checks).ToArray();

        return new OfficialConformanceImportResult
        {
            Assessment = new OfficialConformanceAssessment
            {
                VerificationStatus = verificationStatus,
                SourceEvidenceSha256 = CanonicalJsonDigest.ComputeText(json),
                Runner = document.Runner,
                RequirementSet = document.RequirementSet,
                Target = document.Target,
                RunState = document.Run.State,
                Passed = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Success),
                CreditedPassed = creditEligible
                    ? scenarios
                        .Where(scenario => scenario.ScoringDisposition == OfficialConformanceScoringDisposition.Required)
                        .Sum(scenario => scenario.Passed)
                    : 0,
                Failed = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Failure),
                Warnings = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Warning),
                Skipped = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Skipped),
                Informational = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Info),
                EvidenceSummary = ValidationEvidenceSummarizer.Summarize(coverage),
                Scenarios = scenarios
            },
            Coverage = coverage,
            Observations = observations
        };
    }

    private static void ValidateJsonShape(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = OfficialConformanceContract.MaximumDocumentDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "documentType", out var documentType) ||
                !TryGetString(root, "documentSchemaVersion", out var schemaVersion) ||
                !string.Equals(documentType, ArtifactContracts.OfficialConformanceEvidenceDocumentType, StringComparison.Ordinal) ||
                !ArtifactContracts.IsCompatible(documentType, schemaVersion))
            {
                throw Invalid("Official conformance evidence has an unsupported document contract.");
            }
        }
        catch (OfficialConformanceEvidenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid("Official conformance evidence is not well-formed JSON.", exception);
        }
    }

    private static void ValidateDocument(
        OfficialConformanceEvidenceDocument document,
        OfficialConformanceImportContext? context)
    {
        RequireText(document.Runner.PackageName, "runner package name");
        if (!string.Equals(document.Runner.PackageName, OfficialConformanceContract.PackageName, StringComparison.Ordinal))
        {
            throw Invalid("Official conformance evidence was not produced by the official conformance package.");
        }

        RequireText(document.Runner.Version, "runner version");
        RequireGitCommit(document.Runner.SourceCommit, "runner source commit");
        RequireSha256(document.Runner.PackageSha256, "runner package digest");
        RequireText(document.RequirementSet.Revision, "requirement-set revision");
        RequireSha256(document.RequirementSet.Sha256, "requirement-set digest");
        RequireSha256(document.Target.IdentitySha256, "target identity digest");
        RequireText(document.Target.Transport, "target transport");
        RequireText(document.Target.ProtocolVersion, "target protocol version");
        RequireText(document.Invocation.Mode, "invocation mode");

        if (document.Run.EndedAtUtc < document.Run.StartedAtUtc)
        {
            throw Invalid("Official conformance evidence has an invalid run time range.");
        }

        if (context?.ExpectedTargetIdentitySha256 is { } targetIdentity &&
            !string.Equals(document.Target.IdentitySha256, targetIdentity, StringComparison.Ordinal))
        {
            throw Invalid("Official conformance evidence does not match the validation target identity.");
        }

        if (context?.ExpectedProtocolVersion is { } protocolVersion &&
            !string.Equals(document.Target.ProtocolVersion, protocolVersion, StringComparison.Ordinal))
        {
            throw Invalid("Official conformance evidence does not match the negotiated protocol version.");
        }

        if (document.Invocation.SanitizedArguments.Count > 128 ||
            document.Invocation.SanitizedArguments.Any(argument => argument.Length > OfficialConformanceContract.MaximumTextLength))
        {
            throw Invalid("Official conformance invocation metadata exceeds supported limits.");
        }

        if (document.Scenarios.Count > OfficialConformanceContract.MaximumScenarios)
        {
            throw Invalid("Official conformance evidence contains too many scenarios.");
        }

        var totalChecksBytes = 0;
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in document.Scenarios)
        {
            RequireText(scenario.ScenarioId, "scenario ID");
            if (!scenarioIds.Add(scenario.ScenarioId))
            {
                throw Invalid("Official conformance evidence contains duplicate scenario IDs.");
            }

            if (scenario.ScoringDisposition == OfficialConformanceScoringDisposition.NotScored &&
                string.IsNullOrWhiteSpace(scenario.NotScoredReason))
            {
                throw Invalid("A not-scored official conformance scenario is missing its reason.");
            }

            RequireSha256(scenario.ChecksSha256, "scenario checks digest");
            byte[] checksJson;
            try
            {
                checksJson = Convert.FromBase64String(scenario.ChecksBase64);
            }
            catch (FormatException exception)
            {
                throw Invalid("Official conformance scenario checks are not valid base64.", exception);
            }

            if (!string.Equals(Convert.ToBase64String(checksJson), scenario.ChecksBase64, StringComparison.Ordinal))
            {
                throw Invalid("Official conformance scenario checks are not canonically encoded.");
            }

            totalChecksBytes = checked(totalChecksBytes + checksJson.Length);
            if (totalChecksBytes > OfficialConformanceContract.MaximumEvidenceBytes)
            {
                throw Invalid("Official conformance scenario checks exceed the supported size limit.");
            }

            if (!string.Equals(scenario.ChecksSha256, CanonicalJsonDigest.ComputeBytes(checksJson), StringComparison.Ordinal))
            {
                throw Invalid("Official conformance scenario checks do not match their recorded digest.");
            }

            try
            {
                scenario.Checks = JsonSerializer.Deserialize<List<OfficialConformanceCheckRecord>>(
                    checksJson,
                    OfficialConformanceContract.CreateJsonOptions())
                    ?? throw Invalid("Official conformance scenario checks are empty.");
            }
            catch (OfficialConformanceEvidenceException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw Invalid("Official conformance scenario checks are not valid JSON.", exception);
            }

            if (scenario.Checks.Count > OfficialConformanceContract.MaximumChecksPerScenario)
            {
                throw Invalid("Official conformance evidence contains too many checks in a scenario.");
            }

            foreach (var check in scenario.Checks)
            {
                RequireText(check.Id, "check ID");
                RequireText(check.Name, "check name");
                RequireText(check.Description, "check description");
                if (HasMissingPrerequisite(check) && check.Status is not (OfficialConformanceCheckStatus.Failure or OfficialConformanceCheckStatus.Warning))
                    throw Invalid("Official conformance prerequisite metadata contradicts the check status.");
                if (check.ErrorMessage?.Length > OfficialConformanceContract.MaximumErrorDetailLength)
                {
                    throw Invalid("Official conformance error detail exceeds the supported limit.");
                }

                foreach (var reference in check.SpecReferences)
                {
                    RequireText(reference.Id, "specification reference ID");
                    if (reference.Url is { Length: > OfficialConformanceContract.MaximumTextLength })
                    {
                        throw Invalid("Official conformance specification reference exceeds the supported limit.");
                    }
                }
            }
        }

        if (OfficialConformanceContract.IsPinnedRequirementSet(document.RequirementSet))
        {
            ValidatePinnedServerScenarios(document, scenarioIds);
        }
    }

    private static void ValidatePinnedServerScenarios(
        OfficialConformanceEvidenceDocument document,
        IReadOnlySet<string> scenarioIds)
    {
        if (!string.Equals(document.Invocation.Mode, "server", StringComparison.Ordinal))
        {
            throw Invalid("Pinned official conformance evidence must use the server requirement-set invocation.");
        }

        var required = OfficialConformanceContract.RequiredServerScenarios.ToHashSet(StringComparer.Ordinal);
        var notScored = OfficialConformanceContract.NotScoredServerScenarios
            .ToDictionary(scenario => scenario.ScenarioId, StringComparer.Ordinal);
        foreach (var scenario in document.Scenarios)
        {
            if (required.Contains(scenario.ScenarioId))
            {
                if (scenario.ScoringDisposition != OfficialConformanceScoringDisposition.Required ||
                    !string.IsNullOrWhiteSpace(scenario.NotScoredReason) ||
                    !string.IsNullOrWhiteSpace(scenario.ExtensionId))
                {
                    throw Invalid("Official conformance evidence misclassifies a required scenario.");
                }

                continue;
            }

            if (!notScored.TryGetValue(scenario.ScenarioId, out var pinned))
            {
                throw Invalid("Official conformance evidence contains a scenario outside the pinned requirement set.");
            }

            if (scenario.ScoringDisposition != OfficialConformanceScoringDisposition.NotScored ||
                !string.Equals(scenario.NotScoredReason, pinned.Reason, StringComparison.Ordinal) ||
                !string.Equals(scenario.ExtensionId, pinned.ExtensionId, StringComparison.Ordinal))
            {
                throw Invalid("Official conformance evidence misclassifies a not-scored scenario.");
            }
        }

        if (document.Run.State != OfficialConformanceRunState.Completed)
        {
            return;
        }

        var missing = required.Concat(notScored.Keys)
            .Where(scenarioId => !scenarioIds.Contains(scenarioId))
            .ToArray();
        if (missing.Length > 0)
        {
            throw Invalid("Completed official conformance evidence is missing pinned scenarios.");
        }

        if (document.Scenarios.Any(scenario => scenario.Checks.Count == 0))
        {
            throw Invalid("Completed official conformance evidence contains a scenario without check evidence.");
        }
    }

    private static OfficialConformanceScenarioAssessment ProjectScenario(
        OfficialConformanceScenarioRecord scenario,
        bool verified,
        ICollection<ValidationCoverageDeclaration> coverage,
        ICollection<ValidationObservation> observations)
    {
        var checks = scenario.Checks
            .Select((check, index) => ProjectCheck(scenario, check, index, verified, coverage, observations))
            .ToArray();

        return new OfficialConformanceScenarioAssessment
        {
            ScenarioId = scenario.ScenarioId,
            ExtensionId = scenario.ExtensionId,
            ScoringDisposition = scenario.ScoringDisposition,
            NotScoredReason = scenario.NotScoredReason,
            Passed = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Success),
            Failed = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Failure),
            Warnings = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Warning),
            Skipped = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Skipped),
            Informational = checks.Count(check => check.Status == OfficialConformanceCheckStatus.Info),
            Checks = checks
        };
    }

    private static OfficialConformanceCheckAssessment ProjectCheck(
        OfficialConformanceScenarioRecord scenario,
        OfficialConformanceCheckRecord check,
        int sourceIndex,
        bool verified,
        ICollection<ValidationCoverageDeclaration> coverage,
        ICollection<ValidationObservation> observations)
    {
        var hasError = !string.IsNullOrEmpty(check.ErrorMessage);
        var prerequisiteMissing = HasMissingPrerequisite(check);
        var notApplicable = IsOptionalSseObservation(scenario, check, verified);
        var evidenceId = OfficialConformanceContract.GetCheckEvidenceId(scenario.ScenarioId, check.Id, sourceIndex);
        observations.Add(new ValidationObservation
        {
            Id = evidenceId,
            LayerId = LayerId,
            Component = check.Name,
            ObservationKind = "official-conformance-check",
            ScenarioId = scenario.ScenarioId,
            Lane = EvaluationLane.ExternalConformance,
            Origin = EvidenceOrigin.ExternalConformanceCheck,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["checkId"] = check.Id,
                ["status"] = check.Status.ToString(),
                ["verificationStatus"] = verified ? "verified" : "unverified"
            }
        });

        coverage.Add(prerequisiteMissing
            ? Coverage(evidenceId, ValidationCoverageStatus.Inconclusive, ValidationOutcome.Inconclusive, "The pinned runner could not exercise this check because a prerequisite was missing.")
            : notApplicable
            ? Coverage(evidenceId, ValidationCoverageStatus.NotApplicable, ValidationOutcome.NotApplicable, "The pinned runner observed JSON responses; optional SSE-stream behavior was not applicable.")
            : CreateCoverage(check, verified, evidenceId));
        return new OfficialConformanceCheckAssessment
        {
            SourceIndex = sourceIndex,
            PrerequisiteMissing = prerequisiteMissing,
            Applicability = notApplicable ? AssuranceApplicability.NotApplicable : AssuranceApplicability.Applicable,
            Id = check.Id,
            Name = check.Name,
            Description = check.Description,
            Status = check.Status,
            SpecReferences = check.SpecReferences,
            HasErrorDetail = hasError,
            ErrorDetailSha256 = hasError ? CanonicalJsonDigest.ComputeText(check.ErrorMessage!) : null
        };
    }

    private static bool HasMissingPrerequisite(OfficialConformanceCheckRecord check) =>
        check.Details is { ValueKind: JsonValueKind.Object } details && details.TryGetProperty("untestable", out var untestable) &&
        untestable.ValueKind == JsonValueKind.True;

    private static bool IsOptionalSseObservation(OfficialConformanceScenarioRecord scenario, OfficialConformanceCheckRecord check, bool verified)
    {
        if (!verified || scenario.ScenarioId != "server-sse-multiple-streams" || check.Id != "server-sse-streams-functional" ||
            check.Status != OfficialConformanceCheckStatus.Info || check.Details is not { ValueKind: JsonValueKind.Object } details ||
            !scenario.Checks.Any(candidate => candidate.Id == "server-accepts-multiple-post-streams" &&
                candidate.Status is OfficialConformanceCheckStatus.Success or OfficialConformanceCheckStatus.Failure) ||
            !details.TryGetProperty("numSseStreams", out var streams) || streams.ValueKind != JsonValueKind.Number ||
            !streams.TryGetInt32(out var streamCount) || streamCount != 0 ||
            !details.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 3)
            return false;
        var indices = new HashSet<int>();
        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "json" ||
                !result.TryGetProperty("skipped", out var skipped) || skipped.ValueKind != JsonValueKind.True ||
                !result.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number ||
                !index.TryGetInt32(out var indexValue) || indexValue is < 0 or >= 3 || !indices.Add(indexValue)) return false;
        }
        return true;
    }

    private static ValidationCoverageDeclaration CreateCoverage(
        OfficialConformanceCheckRecord check,
        bool verified,
        string scope)
    {
        if (!verified)
        {
            return Coverage(scope, ValidationCoverageStatus.Inconclusive, ValidationOutcome.Inconclusive,
                "Official conformance provenance is not pinned and verified.");
        }

        return check.Status switch
        {
            OfficialConformanceCheckStatus.Success => Coverage(scope, ValidationCoverageStatus.Covered, ValidationOutcome.Succeeded),
            OfficialConformanceCheckStatus.Failure => Coverage(scope, ValidationCoverageStatus.Covered, ValidationOutcome.Failed),
            OfficialConformanceCheckStatus.Skipped => Coverage(scope, ValidationCoverageStatus.Skipped, ValidationOutcome.Skipped,
                "The official conformance runner skipped this check."),
            OfficialConformanceCheckStatus.Warning => Coverage(scope, ValidationCoverageStatus.Inconclusive, ValidationOutcome.Inconclusive,
                "The official conformance runner reported a warning."),
            OfficialConformanceCheckStatus.Info => Coverage(scope, ValidationCoverageStatus.Inconclusive, ValidationOutcome.Inconclusive,
                "The official conformance runner reported informational evidence only."),
            _ => throw Invalid("Official conformance evidence contains an unknown check status.")
        };
    }

    private static ValidationCoverageDeclaration Coverage(
        string scope,
        ValidationCoverageStatus status,
        ValidationOutcome outcome,
        string? reason = null) => new()
        {
            LayerId = LayerId,
            Scope = scope,
            Status = status,
            ObservedOutcome = outcome,
            Lane = EvaluationLane.ExternalConformance,
            Origin = EvidenceOrigin.ExternalConformanceCheck,
            Confidence = EvidenceConfidenceLevel.High,
            Reason = reason
        };

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > OfficialConformanceContract.MaximumTextLength)
        {
            throw Invalid($"Official conformance evidence has an invalid {field}.");
        }
    }

    private static void RequireSha256(string? value, string field)
    {
        if (value is not { Length: 64 } || value.Any(character => !char.IsAsciiHexDigit(character)) ||
            !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw Invalid($"Official conformance evidence has an invalid {field}.");
        }
    }

    private static void RequireGitCommit(string? value, string field)
    {
        if (value is not { Length: 40 } || value.Any(character => !char.IsAsciiHexDigit(character)) ||
            !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw Invalid($"Official conformance evidence has an invalid {field}.");
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            (value = property.GetString() ?? string.Empty).Length > 0;
    }

    private static OfficialConformanceEvidenceException Invalid(string message, Exception? innerException = null) =>
        new(message, innerException);
}