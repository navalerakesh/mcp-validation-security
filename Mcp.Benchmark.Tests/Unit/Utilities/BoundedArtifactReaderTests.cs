using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class BoundedArtifactReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mcpval-artifact-reader-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"unknown-secret-name\":\"secret-value\"}")]
    [InlineData("{\"server\":null}")]
    [InlineData("{\"validation\":{\"categories\":null}}")]
    [InlineData("{\"server\":{\"endpoint\":\"first\",\"Endpoint\":\"second\"}}")]
    [InlineData("{\"server\":{\"Headers\":{},\"headers\":{}}}")]
    [InlineData("{\"server\":{\"headers\":{\"X-Route\":\"first\",\"x-route\":\"second\"}}}")]
    [InlineData("{\"server\":{\"headers\":{\"X-Route\":null}}}")]
    [InlineData("{\"policy\":{\"suppressions\":[null]}}")]
    [InlineData("{\"server\":{\"timeoutMs\":\"secret-value\"}}")]
    [InlineData("{\"server\":{\"unknown-secret-name\":true}}")]
    [InlineData("{\"server\":{\"transport\":null}}")]
    [InlineData("{\"server\":{\"endpoint\":\"secret-value\"},}")]
    public async Task ReadConfigurationAsync_ShouldRejectAmbiguousOrMalformedInputWithoutEchoingValues(string json)
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, json);

        var read = () => BoundedArtifactReader.ReadConfigurationAsync(file);

        var failure = await read.Should().ThrowAsync<CliUsageException>();
        failure.Which.ToString().Should().NotContain("secret-value").And.NotContain("unknown-secret-name");
    }

    [Theory]
    [InlineData("server", "headers")]
    [InlineData("Server", "Headers")]
    public async Task ReadConfigurationAsync_ShouldAcceptFieldAliasesAndPreserveDictionaryKeyCase(string serverKey, string headersKey)
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, $$$$"""
            {"{{{{serverKey}}}}":{"endpoint":"https://example.invalid/mcp","{{{{headersKey}}}}":{"X-Route":"route"},"Environment":{"KEY":"upper","key":"lower"}}}
            """);

        var result = await BoundedArtifactReader.ReadConfigurationAsync(file);

        result.Server.Endpoint.Should().Be("https://example.invalid/mcp");
        result.Server.Headers.Should().ContainKey("X-Route").WhoseValue.Should().Be("route");
        result.Server.Environment.Should().HaveCount(2).And.ContainKey("KEY").And.ContainKey("key");
    }

    [Fact]
    public async Task ReadConfigurationAsync_ShouldAcceptSerializedDefaults()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, JsonSerializer.Serialize(new McpValidatorConfiguration()));

        var result = await BoundedArtifactReader.ReadConfigurationAsync(file);

        result.Server.Should().NotBeNull();
        result.Validation.Categories.Should().NotBeNull();
    }

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

    [Fact]
    public async Task ReadProviderHarnessAsync_ShouldLoadCompatibleBoundedContract()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "provider-harness.json");
        var manifest = CreateProviderHarness();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        }));

        var actual = await BoundedArtifactReader.ReadProviderHarnessAsync(new FileInfo(path));

        actual.ProviderId.Should().Be(manifest.ProviderId);
        actual.Authorization.SigningKeyId.Should().Be(manifest.Authorization.SigningKeyId);
    }

    [Fact]
    public async Task ReadProviderHarnessAsync_ShouldRejectIncompatibleAndOversizedFiles()
    {
        Directory.CreateDirectory(_root);
        var incompatiblePath = Path.Combine(_root, "provider-incompatible.json");
        await File.WriteAllTextAsync(incompatiblePath, "{\"documentType\":\"mcpval.provider-harness\",\"documentSchemaVersion\":\"3.0.0\"}");
        var malformedPath = Path.Combine(_root, "provider-malformed.json");
        await File.WriteAllTextAsync(malformedPath, "{\"documentType\":\"mcpval.provider-harness\",\"documentSchemaVersion\":\"2.0.0\"}");
        var oversizedPath = Path.Combine(_root, "provider-oversized.json");
        await using (var stream = new FileStream(oversizedPath, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(BoundedArtifactReader.MaximumProviderHarnessBytes + 1L);
        }

        var incompatible = () => BoundedArtifactReader.ReadProviderHarnessAsync(new FileInfo(incompatiblePath));
        var malformed = () => BoundedArtifactReader.ReadProviderHarnessAsync(new FileInfo(malformedPath));
        var oversized = () => BoundedArtifactReader.ReadProviderHarnessAsync(new FileInfo(oversizedPath));

        await incompatible.Should().ThrowAsync<CliUsageException>();
        await malformed.Should().ThrowAsync<CliUsageException>().WithMessage("*not valid JSON for the supported contract*");
        await oversized.Should().ThrowAsync<CliUsageException>().WithMessage("*exceeds*");
    }

    private static ProviderHarnessManifest CreateProviderHarness() => new()
    {
        ProviderId = "provider-test",
        TargetIdentitySha256 = new string('a', 64),
        ProtocolRevision = "2026-07-28",
        Authorization = new ProviderAuthorizationGrant
        {
            GrantId = "grant",
            Authority = "owner",
            TargetIdentitySha256 = new string('a', 64),
            ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            AllowedExecutionModes = [ExecutionMode.Standard],
            AllowedProbeClasses = ["protocol-active"],
            GrantDocumentSha256 = new string('b', 64),
            SigningKeyId = new string('c', 64),
            SignatureBase64 = "dGVzdA=="
        },
        SigningKeyId = new string('c', 64),
        ManifestDocumentSha256 = new string('d', 64),
        SignatureBase64 = Convert.ToBase64String(new byte[64])
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}