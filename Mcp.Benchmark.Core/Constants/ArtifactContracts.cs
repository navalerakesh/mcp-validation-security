namespace Mcp.Benchmark.Core.Constants;

public static class ArtifactContracts
{
    public const string SchemaVersion = "1.0.0";
    public const string ValidationResultSchemaVersion = "2.0.0";
    public const string AuditManifestSchemaVersion = "2.0.0";
    public const string ClientProfileSummarySchemaVersion = "2.0.0";
    public const string OfficialConformanceEvidenceSchemaVersion = "1.0.0";
    public const string ProviderHarnessSchemaVersion = "2.0.0";
    public const string ValidationResultDocumentType = "mcpval.assurance-result";
    public const string AuditManifestDocumentType = "mcpval.audit-manifest";
    public const string ClientProfileSummaryDocumentType = "mcpval.client-profile-summary";
    public const string OfficialConformanceEvidenceDocumentType = "mcpval.official-conformance-evidence";
    public const string ProviderHarnessDocumentType = "mcpval.provider-harness";
    public const string ModelEvaluationDocumentType = "mcpval.model-evaluation";
    public const string CliResultDocumentType = "mcpval.cli-result";
    public const string CliErrorDocumentType = "mcpval.cli-error";
    public const string ValidationAttestationDocumentType = "mcpval.validation-attestation";
    public const string ModelEvaluationSchemaVersion = "2.0.0";

    public static bool IsCompatible(string documentType, string schemaVersion)
    {
        if (!TryParseVersion(schemaVersion, out var candidateMajor))
        {
            return false;
        }

        var currentVersion = documentType switch
        {
            ValidationResultDocumentType => ValidationResultSchemaVersion,
            AuditManifestDocumentType => AuditManifestSchemaVersion,
            ClientProfileSummaryDocumentType => ClientProfileSummarySchemaVersion,
            OfficialConformanceEvidenceDocumentType => OfficialConformanceEvidenceSchemaVersion,
            ProviderHarnessDocumentType => ProviderHarnessSchemaVersion,
            ModelEvaluationDocumentType => ModelEvaluationSchemaVersion,
            CliResultDocumentType or CliErrorDocumentType or ValidationAttestationDocumentType => SchemaVersion,
            _ => null
        };

        return currentVersion != null &&
            TryParseVersion(currentVersion, out var currentMajor) &&
            candidateMajor == currentMajor;
    }

    private static bool TryParseVersion(string? value, out int major)
    {
        major = 0;
        var segments = value?.Split('.', StringSplitOptions.None);
        return segments is { Length: 3 } &&
            segments.All(segment =>
                segment.Length > 0 &&
                segment.All(char.IsAsciiDigit) &&
                (segment.Length == 1 || segment[0] != '0') &&
                int.TryParse(segment, out _)) &&
            int.TryParse(segments[0], out major);
    }
}