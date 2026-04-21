using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Host-level selection of documented client profiles to evaluate against collected validation evidence.
/// </summary>
public sealed class ClientProfileOptions
{
    /// <summary>
    /// Gets or sets the requested profile identifiers. Supports explicit identifiers or the special value <c>all</c>.
    /// </summary>
    [JsonPropertyName("profiles")]
    public List<string> Profiles { get; set; } = new();
}

/// <summary>
/// Aggregated client compatibility assessments for a validation run.
/// </summary>
public sealed class ClientCompatibilityReport
{
    /// <summary>
    /// Gets or sets the normalized set of profile identifiers requested by the caller.
    /// </summary>
    public List<string> RequestedProfiles { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-profile compatibility assessments.
    /// </summary>
    public List<ClientProfileAssessment> Assessments { get; set; } = new();

    /// <summary>
    /// Gets the number of profiles that passed without warnings.
    /// </summary>
    public int CompatibleCount => Assessments.Count(assessment => assessment.Status == ClientProfileCompatibilityStatus.Compatible);

    /// <summary>
    /// Gets the number of profiles that passed with warnings.
    /// </summary>
    public int WarningCount => Assessments.Count(assessment => assessment.Status == ClientProfileCompatibilityStatus.CompatibleWithWarnings);

    /// <summary>
    /// Gets the number of profiles that were deemed incompatible.
    /// </summary>
    public int IncompatibleCount => Assessments.Count(assessment => assessment.Status == ClientProfileCompatibilityStatus.Incompatible);
}

/// <summary>
/// Compatibility assessment for a single documented client profile.
/// </summary>
public sealed class ClientProfileAssessment
{
    /// <summary>
    /// Gets or sets the stable profile identifier.
    /// </summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the client profile.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the revision of the documented profile pack used for evaluation.
    /// </summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the documentation URL backing the profile definition.
    /// </summary>
    public string DocumentationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how the compatibility interpretation is grounded.
    /// </summary>
    public ClientProfileEvidenceBasis EvidenceBasis { get; set; } = ClientProfileEvidenceBasis.Documented;

    /// <summary>
    /// Gets or sets the overall compatibility status for the profile.
    /// </summary>
    public ClientProfileCompatibilityStatus Status { get; set; } = ClientProfileCompatibilityStatus.Compatible;

    /// <summary>
    /// Gets or sets the concise summary of the profile outcome.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of applicable requirements that passed cleanly.
    /// </summary>
    public int PassedRequirements { get; set; }

    /// <summary>
    /// Gets or sets the number of applicable requirements that produced warnings.
    /// </summary>
    public int WarningRequirements { get; set; }

    /// <summary>
    /// Gets or sets the number of applicable required checks that failed.
    /// </summary>
    public int FailedRequirements { get; set; }

    /// <summary>
    /// Gets or sets the per-requirement compatibility details.
    /// </summary>
    public List<ClientProfileRequirementAssessment> Requirements { get; set; } = new();

    /// <summary>
    /// Gets the human-readable status label for reports and summaries.
    /// </summary>
    public string StatusLabel => Status switch
    {
        ClientProfileCompatibilityStatus.Compatible => "Compatible",
        ClientProfileCompatibilityStatus.CompatibleWithWarnings => "Compatible with warnings",
        ClientProfileCompatibilityStatus.Incompatible => "Incompatible",
        _ => "Unknown"
    };
}

/// <summary>
/// Requirement-level assessment emitted as part of a client profile evaluation.
/// </summary>
public sealed class ClientProfileRequirementAssessment
{
    /// <summary>
    /// Gets or sets the stable requirement identifier within the profile pack.
    /// </summary>
    public string RequirementId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable requirement title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requirement importance.
    /// </summary>
    public ClientProfileRequirementLevel Level { get; set; } = ClientProfileRequirementLevel.Recommended;

    /// <summary>
    /// Gets or sets how the requirement is grounded.
    /// </summary>
    public ClientProfileEvidenceBasis EvidenceBasis { get; set; } = ClientProfileEvidenceBasis.Documented;

    /// <summary>
    /// Gets or sets the outcome for the requirement.
    /// </summary>
    public ClientProfileRequirementOutcome Outcome { get; set; } = ClientProfileRequirementOutcome.Satisfied;

    /// <summary>
    /// Gets or sets the short explanation for the requirement outcome.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the validation rule identifiers that informed this requirement assessment.
    /// </summary>
    public List<string> RuleIds { get; set; } = new();

    /// <summary>
    /// Gets or sets example affected components associated with the outcome.
    /// </summary>
    public List<string> ExampleComponents { get; set; } = new();

    /// <summary>
    /// Gets or sets the remediation guidance for this requirement, if any.
    /// </summary>
    public string? Recommendation { get; set; }

    /// <summary>
    /// Gets or sets the documentation URL for this requirement.
    /// </summary>
    public string? DocumentationUrl { get; set; }
}

/// <summary>
/// Overall compatibility status for a documented client profile.
/// </summary>
public enum ClientProfileCompatibilityStatus
{
    Compatible = 0,
    CompatibleWithWarnings = 1,
    Incompatible = 2
}

/// <summary>
/// Requirement severity within a client profile definition.
/// </summary>
public enum ClientProfileRequirementLevel
{
    Required = 0,
    Recommended = 1,
    Informational = 2
}

/// <summary>
/// Requirement outcome classification.
/// </summary>
public enum ClientProfileRequirementOutcome
{
    Satisfied = 0,
    Warning = 1,
    Failed = 2,
    NotApplicable = 3
}

/// <summary>
/// Provenance of the client compatibility interpretation.
/// </summary>
public enum ClientProfileEvidenceBasis
{
    Documented = 0,
    Observed = 1,
    Inferred = 2
}