using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Constants;

public static class OfficialConformanceContract
{
    public const int MaximumEvidenceBytes = 16 * 1024 * 1024;
    public const int MaximumDocumentDepth = 64;
    public const int MaximumScenarios = 512;
    public const int MaximumChecksPerScenario = 8192;
    public const int MaximumTextLength = 4096;
    public const int MaximumErrorDetailLength = 64 * 1024;

    public const string PackageName = "@modelcontextprotocol/conformance";
    public const string PackageVersion = "0.2.0-alpha.12";
    public const string SourceCommit = "f44482ba17df816d3176962a11cdf36aec9bda00";
    public const string PackageSha256 = "6cd489231fe6a90de07ce940b1945ffef7d11063d17558b0a4c12a00f0e6f41f";
    public const string PackageIntegrity = "sha512-eXtZFgmtZiU5SsvXty+RGXGnyIqEVm3GLU/HZmg1wMjtKOocPZZgD9HQKd30oJhio5UB0wFJmsc6r8u48Q+EYA==";
    public const string RequirementSetRevision = "2026-07-28";
    public const string RequirementSetSha256 = "ae2f4f6210fd729e2e318edd5bbfa31a43cee0bc608e48052fa26dbf1d939b57";

    public static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDocumentDepth,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string ComputeChecksSha256(IReadOnlyList<OfficialConformanceCheckRecord> checks) =>
        CanonicalJsonDigest.ComputeObject(checks, CreateJsonOptions());

    public static bool IsPinnedRunner(OfficialConformanceRunnerIdentity runner) =>
        string.Equals(runner.PackageName, PackageName, StringComparison.Ordinal) &&
        string.Equals(runner.Version, PackageVersion, StringComparison.Ordinal) &&
        string.Equals(runner.SourceCommit, SourceCommit, StringComparison.Ordinal) &&
        string.Equals(runner.PackageSha256, PackageSha256, StringComparison.Ordinal) &&
        string.Equals(runner.PackageIntegrity, PackageIntegrity, StringComparison.Ordinal);

    public static bool IsPinnedRequirementSet(OfficialConformanceRequirementSet requirementSet) =>
        string.Equals(requirementSet.Revision, RequirementSetRevision, StringComparison.Ordinal) &&
        string.Equals(requirementSet.Sha256, RequirementSetSha256, StringComparison.Ordinal);
}