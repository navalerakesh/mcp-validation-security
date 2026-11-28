using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;
using System.Security.Cryptography;

namespace Mcp.Benchmark.Core.Services;

public static class ControlledIdentitySecurityBaseline
{
    private static readonly string[] ControlIds = ["actor-binding", "scope-enforcement", "actor-action-object-authorization"];
    public static IReadOnlyList<string> RequiredClaimIds { get; } = Array.AsReadOnly(ControlIds.Select(controlId => $"security-baseline:{controlId}").ToArray());

    public static void VerifyRetainedRequirement(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var requirement = result.Assurance.RequiredIdentityControls;
        if (requirement == null) return;
        try
        {
            VerifyRequirement(requirement, result.ValidationId, result.Run.TargetIdentitySha256 ?? string.Empty,
                ValidationAssessmentClock.GetTimestamp(result));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException("Retained identity baseline evidence is invalid.");
        }
    }

    public static ControlledIdentityMatrixAssessment VerifyRequirement(ControlledIdentitySecurityRequirement requirement,
        string runId, string targetIdentitySha256, DateTimeOffset assessedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (requirement.Contract == null || requirement.Admission == null || requirement.SignedEvidenceJson == null ||
            requirement.TrustedObserverPublicKeyPem == null || requirement.Admission.RunId != runId ||
            requirement.Admission.TargetIdentitySha256 != targetIdentitySha256 ||
            !PemEncoding.TryFind(requirement.TrustedObserverPublicKeyPem, out var pem) ||
            !requirement.TrustedObserverPublicKeyPem.AsSpan(pem.Label).SequenceEqual("PUBLIC KEY") ||
            !requirement.TrustedObserverPublicKeyPem.Trim().AsSpan().SequenceEqual(requirement.TrustedObserverPublicKeyPem.AsSpan(pem.Location)))
            throw new InvalidDataException("Required identity baseline admission is invalid.");
        var verified = ControlledIdentityEvidenceVerifier.Verify(requirement.SignedEvidenceJson, requirement.Contract,
            requirement.Admission, requirement.TrustedObserverPublicKeyPem, assessedAt, cancellationToken);
        if (verified.EvidenceIntegrityOutcome != ValidationOutcome.Succeeded || verified.RecordedGaps.Count != 0)
            throw new InvalidDataException("Required identity baseline contains inadmissible observation data.");
        return verified;
    }

    public static bool HasCompleteSelection(ControlledIdentitySecurityRequirement requirement)
    {
        var selection = new[] { requirement.ActorBindingCaseId, requirement.ScopeEnforcementCaseId, requirement.AuthorizationCaseId };
        return selection.All(ControlledEvidenceAdmission.IsValidIdentifier) && selection.Distinct(StringComparer.Ordinal).Count() == 3 &&
            MatchesChallenge(requirement.Contract, requirement.ActorBindingCaseId, ControlledIdentityChallenge.ActorSubstitution) &&
            MatchesChallenge(requirement.Contract, requirement.ScopeEnforcementCaseId, ControlledIdentityChallenge.InsufficientScope) &&
            MatchesChallenge(requirement.Contract, requirement.AuthorizationCaseId, ControlledIdentityChallenge.UnauthorizedActorOrObject);
    }

    private static bool MatchesChallenge(ControlledIdentityMatrixContract contract, string? caseId, ControlledIdentityChallenge challenge)
    {
        var entries = contract.Cases.Where(entry => entry.CaseId == caseId).Take(2).ToArray();
        if (entries.Length != 1) return false;
        var entry = entries[0];
        if (entry.IsPositiveControl || entry.ExpectedDecision != ControlledAuthorizationDecision.Denied || entry.SecurityChallenge != challenge) return false;
        var control = contract.Cases.SingleOrDefault(candidate => candidate.CaseId == entry.PositiveControlCaseId);
        if (control is not { IsPositiveControl: true }) return false;
        return challenge switch
        {
            ControlledIdentityChallenge.ActorSubstitution => entry.IdentityRole == ControlledIdentityRole.CallerAssertion &&
                entry.ExpectedEffectiveActorAlias == control.ExpectedEffectiveActorAlias && entry.Binding.ActorAlias == control.Binding.ActorAlias &&
                entry.Binding.ObjectAlias == control.Binding.ObjectAlias && entry.Binding.ArgumentsSha256 != control.Binding.ArgumentsSha256,
            ControlledIdentityChallenge.InsufficientScope => entry.ExpectedEffectiveActorAlias == control.ExpectedEffectiveActorAlias &&
                entry.Binding.ActorAlias == control.Binding.ActorAlias && entry.Binding.ObjectAlias == control.Binding.ObjectAlias &&
                entry.Binding.ArgumentsSha256 == control.Binding.ArgumentsSha256,
            ControlledIdentityChallenge.UnauthorizedActorOrObject => entry.Binding.ActorAlias != control.Binding.ActorAlias || entry.Binding.ObjectAlias != control.Binding.ObjectAlias,
            _ => false
        };
    }

    public static IReadOnlyList<AssuranceClaim> Project(ValidationResult result, ControlledIdentitySecurityRequirement? requirement)
        => Project(result, requirement, out _);

    internal static IReadOnlyList<AssuranceClaim> Project(ValidationResult result, ControlledIdentitySecurityRequirement? requirement,
        out ControlledIdentitySecurityRequirement? verifiedRequirement)
    {
        ArgumentNullException.ThrowIfNull(result);
        verifiedRequirement = null;
        ControlledIdentityMatrixAssessment? replay = null;
        string? evidenceId = null;
        string? contractDigest = null;
        var intact = false;
        if (requirement != null)
        {
            var clock = ValidationAssessmentClock.GetTimestamp(result);
            ControlledIdentityMatrixAssessment? verified = null;
            try
            {
                verified = VerifyRequirement(requirement, result.ValidationId, result.Run.TargetIdentitySha256 ?? string.Empty, clock);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException) { }
            if (verified?.AdmittedContract is { } admitted)
            {
                verifiedRequirement = requirement with { Contract = requirement.Contract with { Cases = Array.AsReadOnly(requirement.Contract.Cases.ToArray()) } };
                contractDigest = CanonicalJsonDigest.ComputeObject(admitted);
                var matches = result.Assessments.Scenarios.Where(scenario => scenario.ControlledIdentity is { } assessment &&
                    assessment.SourceEvidenceSha256 == verified.SourceEvidenceSha256).Take(2).ToArray();
                if (matches.Length == 1)
                {
                    var assessment = matches[0].ControlledIdentity!;
                    replay = verified;
                    intact = CanonicalJsonDigest.ComputeObject(assessment.RecordedObservations) == CanonicalJsonDigest.ComputeObject(verified.RecordedObservations) &&
                        CanonicalJsonDigest.ComputeObject(assessment.RecordedGaps) == CanonicalJsonDigest.ComputeObject(verified.RecordedGaps) &&
                        CanonicalJsonDigest.ComputeObject(assessment.AdmittedContract) == contractDigest;
                    evidenceId = matches[0].ScenarioId;
                }
            }
        }

        var selection = new[] { requirement?.ActorBindingCaseId, requirement?.ScopeEnforcementCaseId, requirement?.AuthorizationCaseId };
        var distinctSelection = selection.All(ControlledEvidenceAdmission.IsValidIdentifier) && selection.Distinct(StringComparer.Ordinal).Count() == 3;

        return
        [
            Claim(ControlIds[0], requirement?.ActorBindingCaseId, ControlledIdentityChallenge.ActorSubstitution, assessment => [assessment.EffectiveActorOutcome, assessment.AuthenticationBindingOutcome, assessment.RoleBindingOutcome]),
            Claim(ControlIds[1], requirement?.ScopeEnforcementCaseId, ControlledIdentityChallenge.InsufficientScope, assessment => [assessment.ScopeOutcome]),
            Claim(ControlIds[2], requirement?.AuthorizationCaseId, ControlledIdentityChallenge.UnauthorizedActorOrObject, assessment => [assessment.DecisionOutcome])
        ];

        AssuranceClaim Claim(string controlId, string? caseId, ControlledIdentityChallenge challenge, Func<ControlledIdentityCaseAssessment, IEnumerable<ValidationOutcome>> outcomes)
        {
            var declared = distinctSelection ? replay?.AdmittedContract?.Cases.SingleOrDefault(entry => entry.CaseId == caseId) : null;
            var selected = declared != null && MatchesChallenge(replay!.AdmittedContract!, caseId, challenge)
                ? replay?.Cases.SingleOrDefault(entry => entry.CaseId == caseId) : null;
            var control = replay?.Cases.SingleOrDefault(entry => entry.CaseId == declared?.PositiveControlCaseId);
            var predicates = selected == null ? [] : outcomes(selected).Append(selected.DecisionOutcome).ToArray();
            var violated = predicates.Contains(ValidationOutcome.Failed);
            var complete = intact && replay is { EvidenceIntegrityOutcome: ValidationOutcome.Succeeded } &&
                selected is { ObservationOutcome: ValidationOutcome.Succeeded, PositiveControlOutcome: ValidationOutcome.Succeeded } &&
                control?.Outcome == ValidationOutcome.Succeeded &&
                predicates.All(outcome => outcome is ValidationOutcome.Succeeded or ValidationOutcome.NotApplicable);
            return new AssuranceClaim
            {
                ClaimId = $"security-baseline:{controlId}", Subject = controlId,
                Dimension = AssuranceDimension.Security, RequirementLevel = AssuranceRequirementLevel.SecurityControl,
                Authority = AssuranceAuthority.DeterministicSecurity,
                Source = new AssuranceSourceReference { Revision = replay?.AdmittedContract?.PolicyRevision ?? AssuranceProfileCatalog.ComprehensiveRevision, Sha256 = contractDigest },
                Outcome = violated ? AssuranceClaimOutcome.Violated : complete ? AssuranceClaimOutcome.Satisfied : AssuranceClaimOutcome.Incomplete,
                Confidence = violated || complete ? EvidenceConfidenceLevel.High : EvidenceConfidenceLevel.None,
                EvidenceIds = evidenceId == null ? [] : [evidenceId],
                Summary = violated ? "The selected identity matrix records a failed security control."
                    : complete ? "The security control is satisfied within the selected identity matrix only."
                    : "The selected security control lacks complete admitted identity evidence.",
                Remediation = "Supply the selected signed observer matrix, including positive and denial controls, bound to this run and target."
            };
        }
    }
}