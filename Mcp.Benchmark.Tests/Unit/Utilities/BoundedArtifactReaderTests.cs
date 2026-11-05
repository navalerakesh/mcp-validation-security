using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class BoundedArtifactReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mcpval-artifact-reader-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadValidationResultAsync_ShouldRequireExplicitCompatibleContract()
    {
        Directory.CreateDirectory(_root);
        var missingPath = Path.Combine(_root, "missing.json");
        var incompatiblePath = Path.Combine(_root, "incompatible.json");
        await File.WriteAllTextAsync(missingPath, "{\"run\":{\"validationId\":\"test\"}}");
        await File.WriteAllTextAsync(incompatiblePath, "{\"documentType\":\"mcpval.validation-result\",\"documentSchemaVersion\":\"1.2.0\"}");

        var missing = () => BoundedArtifactReader.ReadValidationResultAsync(new FileInfo(missingPath));
        var incompatible = () => BoundedArtifactReader.ReadValidationResultAsync(new FileInfo(incompatiblePath));

        await missing.Should().ThrowAsync<CliUsageException>();
        await incompatible.Should().ThrowAsync<CliUsageException>();
    }

    [Fact]
    public async Task ReadValidationResultAsync_ShouldRejectOversizedFileBeforeDeserialization()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "oversized.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(BoundedArtifactReader.MaximumValidationResultBytes + 1L);
        }

        var read = () => BoundedArtifactReader.ReadValidationResultAsync(new FileInfo(path));

        await read.Should().ThrowAsync<CliUsageException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task ReadValidationResultAsync_ShouldLoadExplicitCurrentContract()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "valid.json");
        var expected = new ValidationResult { ValidationId = "validation-reader" };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(expected, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var actual = await BoundedArtifactReader.ReadValidationResultAsync(new FileInfo(path));

        actual.ValidationId.Should().Be(expected.ValidationId);
    }

    [Fact]
    public async Task ReadValidationResultAsync_ShouldDiscardPersistedRuntimeToolHandles()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "runtime-tools.json");
        var expected = new ValidationResult { ValidationId = "validation-runtime-tools" };
        expected.CapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary { DiscoveredToolsCount = 1 }
        };
        var root = JsonSerializer.SerializeToNode(
            expected,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        root["run"]!["capabilitySnapshot"]!["payload"]!["tools"] = new JsonArray(new JsonObject
        {
            ["name"] = "runtime-only-tool",
            ["description"] = "SDK runtime object that is not part of the canonical contract"
        });
        await File.WriteAllTextAsync(path, root.ToJsonString());

        var actual = await BoundedArtifactReader.ReadValidationResultAsync(new FileInfo(path));

        actual.ValidationId.Should().Be(expected.ValidationId);
        actual.CapabilitySnapshot!.Payload!.DiscoveredToolsCount.Should().Be(1);
        actual.CapabilitySnapshot.Payload.Tools.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}