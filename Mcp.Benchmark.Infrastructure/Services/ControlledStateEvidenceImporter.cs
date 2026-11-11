using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ControlledStateEvidenceImporter
{
    public ControlledStateScenarioPack ImportPack(
        string json, ControlledStateScenarioContract admittedContract, string trustedObserverPublicKeyPem, DateTimeOffset assessedAt,
        CancellationToken cancellationToken = default) =>
        new(Import(json, admittedContract, trustedObserverPublicKeyPem, assessedAt, cancellationToken));

    public ControlledStateAssessment Import(
        string json,
        ControlledStateScenarioContract admittedContract,
        string trustedObserverPublicKeyPem,
        DateTimeOffset assessedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(admittedContract);
        ArgumentNullException.ThrowIfNull(trustedObserverPublicKeyPem);
        cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > ExecutionPolicyDefaults.DefaultMaxResponseBytes ||
            Encoding.UTF8.GetByteCount(json) > ExecutionPolicyDefaults.DefaultMaxResponseBytes || trustedObserverPublicKeyPem.Length > 16_384)
            throw Invalid();
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RequireUniqueProperties(parsed.RootElement);
            RequireString(parsed.RootElement, "documentType", ControlledStateEvidenceDocument.Type);
            RequireString(parsed.RootElement, "documentSchemaVersion", ControlledStateEvidenceDocument.Revision);
            RequireString(parsed.RootElement, "signatureAlgorithm", P256ArtifactSignatures.Algorithm);
            cancellationToken.ThrowIfCancellationRequested();
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                MaxDepth = 32
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            var document = parsed.RootElement.Deserialize<ControlledStateEvidenceDocument>(options);
            if (document == null || document.Contract == null || document.Evidence == null ||
                document.IssuedAt == default || document.IssuedAt > assessedAt || document.ExpiresAt <= assessedAt ||
                document.ExpiresAt <= document.IssuedAt ||
                document.Evidence.ObservedThrough == null || document.Evidence.ObservedThrough > document.IssuedAt ||
                document.Evidence.AuditObservedThrough > document.IssuedAt ||
                CanonicalJsonDigest.ComputeObject(document.Contract) != CanonicalJsonDigest.ComputeObject(admittedContract))
                throw Invalid();

            using var key = ECDsa.Create();
            key.ImportFromPem(trustedObserverPublicKeyPem);
            P256ArtifactSignatures.EnsureKey(key);
            var keyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            if (!string.Equals(document.SigningKeyId, keyId, StringComparison.Ordinal) ||
                !P256ArtifactSignatures.TryDecodeSignature(document.SignatureBase64, out var signature) ||
                !key.VerifyHash(Convert.FromHexString(document.ComputePayloadDigest()), signature,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Invalid();

            cancellationToken.ThrowIfCancellationRequested();
            return ControlledStateEvaluator.Evaluate(admittedContract, document.Evidence, admittedContract.Binding,
                admittedContract.ObserverId, assessedAt) with
            {
                SourceEvidenceSha256 = CanonicalJsonDigest.ComputeText(json),
                VerifiedObserverKeyId = keyId
            };
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or ArgumentException or FormatException)
        {
            throw Invalid();
        }
    }

    private static void RequireString(JsonElement root, string name, string expected)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
            throw Invalid();
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) RequireUniqueProperties(value);
        }
    }

    private static InvalidDataException Invalid() => new("Controlled state evidence could not be admitted and verified.");
}