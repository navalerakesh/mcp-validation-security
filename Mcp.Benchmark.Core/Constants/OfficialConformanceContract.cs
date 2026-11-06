using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Constants;

public static class OfficialConformanceContract
{
    private static readonly OfficialConformancePin Pin = LoadPin();

    public const int MaximumEvidenceBytes = 16 * 1024 * 1024;
    public const int MaximumDocumentDepth = 64;
    public const int MaximumScenarios = 512;
    public const int MaximumChecksPerScenario = 8192;
    public const int MaximumTextLength = 4096;
    public const int MaximumErrorDetailLength = 64 * 1024;

    public static string PackageName => Pin.PackageName;
    public static string PackageVersion => Pin.Version;
    public static string SourceCommit => Pin.SourceCommit;
    public static string PackageSha256 => Pin.PackageSha256;
    public static string PackageIntegrity => Pin.PackageIntegrity;
    public static string RequirementSetRevision => Pin.RequirementSet.Revision;
    public static string RequirementSetSha256 => Pin.RequirementSet.Sha256;
    public static IReadOnlyList<string> RequiredServerScenarios => Pin.RequirementSet.RequiredServerScenarios;
    public static IReadOnlyList<OfficialConformancePinnedScenario> NotScoredServerScenarios => Pin.RequirementSet.NotScoredServerScenarios;

    public static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDocumentDepth,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static byte[] SerializeChecks(IReadOnlyList<OfficialConformanceCheckRecord> checks) =>
        JsonSerializer.SerializeToUtf8Bytes(checks, CreateJsonOptions());

    public static string ComputeChecksSha256(ReadOnlySpan<byte> checksJson) =>
        CanonicalJsonDigest.ComputeBytes(checksJson);

    public static bool IsPinnedRunner(OfficialConformanceRunnerIdentity runner) =>
        string.Equals(runner.PackageName, PackageName, StringComparison.Ordinal) &&
        string.Equals(runner.Version, PackageVersion, StringComparison.Ordinal) &&
        string.Equals(runner.SourceCommit, SourceCommit, StringComparison.Ordinal) &&
        string.Equals(runner.PackageSha256, PackageSha256, StringComparison.Ordinal) &&
        string.Equals(runner.PackageIntegrity, PackageIntegrity, StringComparison.Ordinal);

    public static bool IsPinnedRequirementSet(OfficialConformanceRequirementSet requirementSet) =>
        string.Equals(requirementSet.Revision, RequirementSetRevision, StringComparison.Ordinal) &&
        string.Equals(requirementSet.Sha256, RequirementSetSha256, StringComparison.Ordinal);

    private static OfficialConformancePin LoadPin()
    {
        const string resourceName = "Mcp.Benchmark.Core.Resources.official-conformance-pin.json";
        using var stream = typeof(OfficialConformanceContract).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded conformance pin '{resourceName}' is unavailable.");
        return JsonSerializer.Deserialize<OfficialConformancePin>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Embedded conformance pin is invalid.");
    }

    private sealed class OfficialConformancePin
    {
        public required string PackageName { get; init; }
        public required string Version { get; init; }
        public required string SourceCommit { get; init; }
        public required string PackageSha256 { get; init; }
        public required string PackageIntegrity { get; init; }
        public required OfficialConformanceRequirementSetPin RequirementSet { get; init; }
    }

    private sealed class OfficialConformanceRequirementSetPin
    {
        public required string Revision { get; init; }
        public required string Sha256 { get; init; }
        public required IReadOnlyList<string> RequiredServerScenarios { get; init; }
        public required IReadOnlyList<OfficialConformancePinnedScenario> NotScoredServerScenarios { get; init; }
    }
}

public sealed class OfficialConformancePinnedScenario
{
    public required string ScenarioId { get; init; }
    public required string Reason { get; init; }
    public string? ExtensionId { get; init; }
}