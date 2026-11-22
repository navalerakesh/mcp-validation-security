using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Infrastructure.Services;

internal static class SignedControlledEvidenceReader
{
    internal static T Read<T>(string json, string trustedPublicKeyPem, string documentType, string revision,
        DateTimeOffset assessedAt, Func<T, bool> admit, CancellationToken cancellationToken) where T : class, ISignedControlledEvidence
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(trustedPublicKeyPem);
        cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > ExecutionPolicyDefaults.DefaultMaxResponseBytes || Encoding.UTF8.GetByteCount(json) > ExecutionPolicyDefaults.DefaultMaxResponseBytes ||
            trustedPublicKeyPem.Length > 16_384) throw Invalid();
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RequireUniqueProperties(parsed.RootElement);
            RequireString(parsed.RootElement, "documentType", documentType);
            RequireString(parsed.RootElement, "documentSchemaVersion", revision);
            RequireString(parsed.RootElement, "signatureAlgorithm", P256ArtifactSignatures.Algorithm);
            cancellationToken.ThrowIfCancellationRequested();
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            var document = parsed.RootElement.Deserialize<T>(options);
            if (document == null || document.IssuedAt == default || document.IssuedAt > assessedAt || document.ExpiresAt <= assessedAt ||
                document.ExpiresAt <= document.IssuedAt || !admit(document)) throw Invalid();
            using var key = ECDsa.Create();
            key.ImportFromPem(trustedPublicKeyPem);
            P256ArtifactSignatures.EnsureKey(key);
            var keyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo());
            if (document.SigningKeyId != keyId || !P256ArtifactSignatures.TryDecodeSignature(document.SignatureBase64, out var signature) ||
                !key.VerifyHash(Convert.FromHexString(document.ComputePayloadDigest()), signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            return document;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or ArgumentException or FormatException)
        {
            throw Invalid();
        }
    }

    private static void RequireString(JsonElement root, string name, string expected)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || value.GetString() != expected) throw Invalid();
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
            foreach (var value in element.EnumerateArray()) RequireUniqueProperties(value);
    }

    private static InvalidDataException Invalid() => new("Signed controlled evidence could not be admitted and verified.");
}