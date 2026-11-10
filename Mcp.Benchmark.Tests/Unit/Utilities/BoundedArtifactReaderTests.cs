using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class BoundedArtifactReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mcpval-artifact-reader-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"schemaVersion\":\"1.0.0\"}", true)]
    [InlineData("{\"SchemaVersion\":\"1.0.0\",\"Server\":{\"Headers\":{\"X-Route\":\"route\"}}}", true)]
    [InlineData("{\"schemaVersion\":\"2.0.0\"}", false)]
    [InlineData("{\"server\":null}", false)]
    [InlineData("{\"server\":{\"headers\":{\"X-Route\":null}}}", false)]
    [InlineData("{\"extra\":true}", false)]
    [InlineData("{\"execution\":{\"mode\":999}}", false)]
    [InlineData("{\"reporting\":{\"formats\":[999]}}", false)]
    [InlineData("{\"reporting\":{\"formats\":[0,999]}}", false)]
    [InlineData("{\"reporting\":{\"formats\":[0]}}", true)]
    [InlineData("{\"execution\":{\"mode\":\"sAfE\"}}", true)]
    [InlineData("{\"execution\":{\"mode\":0}}", true)]
    [InlineData("{\"server\":{\"protocolEra\":\"modern\"}}", true)]
    [InlineData("{\"server\":{\"protocolEra\":999}}", false)]
    [InlineData("{\"server\\n\":{}}", false)]
    [InlineData("{\"server\\r\":{}}", false)]
    [InlineData("{\"server\\r\\n\":{}}", false)]
    [InlineData("{\"server\\u2028\":{}}", false)]
    [InlineData("{\"server\\u2029\":{}}", false)]
    [InlineData("{\"server\":{\"headers\\n\":{}}}", false)]
    public async Task ConfigurationSchema_ShouldReflectVersionAliasesAndStrictShape(string json, bool valid)
    {
        var schema = JsonSchema.FromText(BoundedArtifactReader.CreateConfigurationSchema().ToJsonString());

        using var document = JsonDocument.Parse(json);
        schema.Evaluate(document.RootElement).IsValid.Should().Be(valid);
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, json);
        if (valid)
            (await BoundedArtifactReader.ReadConfigurationAsync(file)).Should().NotBeNull();
        else
        {
            var read = () => BoundedArtifactReader.ReadConfigurationAsync(file);
            await read.Should().ThrowAsync<CliUsageException>();
        }
    }

    [Fact]
    public void ConfigurationSchema_ShouldAcceptSerializedDefaults()
    {
        var schema = JsonSchema.FromText(BoundedArtifactReader.CreateConfigurationSchema().ToJsonString());

        schema.Evaluate(JsonSerializer.SerializeToElement(new McpValidatorConfiguration())).IsValid.Should().BeTrue();
    }

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
    [InlineData("{\"schemaVersion\":\"secret-value\"}")]
    [InlineData("{\"schemaVersion\":\"2.0.0\"}")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"execution\":{\"mode\":999}}")]
    [InlineData("{\"server\":{\"protocolEra\":999}}")]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"SchemaVersion\":\"1.0.0\"}")]
    [InlineData("{\"server\":{\"schemaVersion\":\"1.0.0\"}}")]
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
    public async Task ReadConfigurationDocumentAsync_ShouldRetainOnlyCanonicalSuppliedSettingPaths()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName,
            "{\"Server\":{\"endpoint\":\"secret-endpoint-value\",\"Headers\":{\"secret-header-name\":\"secret-header-value\"}}}");

        var document = await BoundedArtifactReader.ReadConfigurationDocumentAsync(file);
        var effective = BoundedArtifactReader.GetEffectiveConfigurationSettingPaths(document.Configuration);

        document.SuppliedSettings.Should().Contain("$.server.endpoint").And.Contain("$.server.Headers");
        document.SuppliedSettings.Should().NotContain("$.server.timeoutMs");
        effective.Should().Contain("$.server.timeoutMs").And.BeInAscendingOrder(StringComparer.Ordinal);
        JsonSerializer.Serialize(document.SuppliedSettings).Should().NotContain("secret-");
        JsonSerializer.Serialize(effective).Should().NotContain("secret-");
    }

    [Fact]
    public void ConfigurationOrigins_ShouldRetainExplicitSameValueOverridesAndGovernanceChangesWithoutValues()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Server.Endpoint = "secret-endpoint-value";
        configuration.Validation.Categories.ToolTesting.TestToolExecution = true;
        var tracker = new ConfigurationSourceTracker();
        tracker.RecordFileSettings(["$.server.endpoint", "$.validation.categories.tools.TestToolExecution"]);
        tracker.Record(value => value.Server.Endpoint, ConfigurationSourceKind.CommandLine, "--server", "Explicit CLI setting.");
        tracker.RecordBooleanChanges(configuration, ConfigurationSourceKind.Governance, "safe-mode", "Active probes denied.",
            () => configuration.Validation.Categories.ToolTesting.TestToolExecution = false);

        var origins = tracker.Snapshot(configuration);

        var endpoint = origins.Single(origin => origin.Setting == "$.server.endpoint");
        endpoint.Source.Should().Be(ConfigurationSourceKind.CommandLine);
        endpoint.ReplacedSource.Should().Be(ConfigurationSourceKind.File);
        origins.Single(origin => origin.Setting == "$.validation.categories.tools.TestToolExecution")
            .Source.Should().Be(ConfigurationSourceKind.Governance);
        origins.Single(origin => origin.Setting == "$.server.timeoutMs").Source.Should().Be(ConfigurationSourceKind.Default);
        JsonSerializer.Serialize(origins).Should().NotContain("secret-");
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("SchemaVersion")]
    public async Task ReadConfigurationAsync_ShouldNormalizeSupportedVersionWithoutRuntimeMetadata(string versionProperty)
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName,
            $$$"""{"{{{versionProperty}}}":"1.0.0","server":{"endpoint":"https://example.invalid/mcp"}}""");

        var result = await BoundedArtifactReader.ReadConfigurationAsync(file);

        result.Server.Endpoint.Should().Be("https://example.invalid/mcp");
        JsonSerializer.Serialize(result).Should().NotContain("schemaVersion");
    }

    [Theory]
    [InlineData("{\"execution\":{\"mode\":0},\"server\":{\"protocolEra\":0}}")]
    [InlineData("{\"execution\":{\"mode\":\"safe\"},\"server\":{\"protocolEra\":\"auto\"}}")]
    public async Task ReadConfigurationAsync_ShouldRetainDefinedLegacyEnumRepresentations(string json)
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, json);

        var configuration = await BoundedArtifactReader.ReadConfigurationAsync(file);

        configuration.Execution!.Mode.Should().Be(ExecutionMode.Safe);
        configuration.Server.ProtocolEra.Should().Be(McpProtocolEraSelection.Auto);
    }

    [Fact]
    public void ExecutionPolicyOrigins_ShouldRecordExplicitBudgetsAndEnforcedSafeMode()
    {
        var configuration = new McpValidatorConfiguration();
        var tracker = new ConfigurationSourceTracker();
        tracker.RecordFileSettings(["$.validation.categories.tools.TestToolExecution"]);

        ExecutionPolicyOverrides.Apply(configuration, executionMode: "safe",
            maxRequests: configuration.Execution!.MaxRequests, sources: tracker);
        var origins = tracker.Snapshot(configuration);

        origins.Single(origin => origin.Pointer == "--max-requests").Source.Should().Be(ConfigurationSourceKind.CommandLine);
        var execution = origins.Single(origin => origin.Setting == "$.validation.categories.tools.TestToolExecution");
        execution.Source.Should().Be(ConfigurationSourceKind.Governance);
        execution.ReplacedSource.Should().Be(ConfigurationSourceKind.File);
        configuration.Validation.Categories.ToolTesting.TestToolExecution.Should().BeFalse();
        origins.Single(origin => origin.Setting == "$.server.timeoutMs").Source.Should().Be(ConfigurationSourceKind.Governance);
    }

    [Fact]
    public async Task ReadConfigurationAsync_ShouldAcceptLegacyUtf8ByteOrderMark()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, "{\"server\":{\"endpoint\":\"https://example.invalid/mcp\"}}",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await BoundedArtifactReader.ReadConfigurationAsync(file);

        result.Server.Endpoint.Should().Be("https://example.invalid/mcp");
    }

    [Fact]
    public async Task ReadConfigurationAsync_AddingCurrentVersion_ShouldPreserveNormalizedLegacySettings()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        var original = new McpValidatorConfiguration();
        original.Server.Endpoint = "https://example.invalid/mcp";
        original.Server.Environment["KEY"] = "upper";
        original.Server.Environment["key"] = "lower";
        var document = JsonSerializer.SerializeToNode(original)!.AsObject();
        await File.WriteAllTextAsync(file.FullName, document.ToJsonString());
        var legacy = await BoundedArtifactReader.ReadConfigurationAsync(file);

        document["schemaVersion"] = "1.0.0";
        await File.WriteAllTextAsync(file.FullName, document.ToJsonString());
        var versioned = await BoundedArtifactReader.ReadConfigurationAsync(file);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(legacy), JsonSerializer.SerializeToNode(versioned)).Should().BeTrue();
    }

    [Fact]
    public async Task ReadConfigurationAsync_ShouldRejectInvalidUtf8AsUsageError()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllBytesAsync(file.FullName, [0xff, 0x7b, 0x7d]);

        var read = () => BoundedArtifactReader.ReadConfigurationAsync(file);

        var failure = await read.Should().ThrowAsync<CliUsageException>();
        failure.Which.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ReadConfigurationAsync_ShouldEnforceExactByteBoundary(int extraBytes, bool accepted)
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, "{}" + new string(' ', BoundedArtifactReader.MaximumConfigurationBytes - 2 + extraBytes));

        var read = () => BoundedArtifactReader.ReadConfigurationAsync(file);

        if (accepted)
            (await read()).Should().NotBeNull();
        else
            await read.Should().ThrowAsync<CliUsageException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task ReadConfigurationAsync_ShouldPreserveCallerCancellation()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, "{}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var read = () => BoundedArtifactReader.ReadConfigurationAsync(file, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadText_ShouldRejectZeroLengthBeforeOpeningInput()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "empty.json"));
        await File.WriteAllTextAsync(file.FullName, string.Empty);
        var asynchronous = () => BoundedArtifactReader.ReadTextAsync(file, BoundedArtifactReader.MaximumConfigurationBytes);
        var synchronous = () => BoundedArtifactReader.ReadText(file, BoundedArtifactReader.MaximumConfigurationBytes);

        await asynchronous.Should().ThrowAsync<CliUsageException>().WithMessage("*non-empty declared length*");
        synchronous.Should().Throw<CliUsageException>().WithMessage("*non-empty declared length*");
    }

    [Fact]
    public async Task PreparedConfiguration_ShouldBeConsumedOnceWithoutRereadingMutableInput()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, "{\"server\":{\"endpoint\":\"original\"}}");
        var prepared = await BoundedArtifactReader.ReadConfigurationDocumentAsync(file);
        var session = new CliSessionContext("configuration-snapshot");
        session.PrepareConfiguration(file, prepared);
        await File.WriteAllTextAsync(file.FullName, "{\"server\":{\"endpoint\":\"changed\"}}");

        var first = await session.ReadConfigurationAsync(file);
        var later = await session.ReadConfigurationAsync(file);

        first.Should().BeSameAs(prepared);
        first.Configuration.Server.Endpoint.Should().Be("original");
        later.Configuration.Server.Endpoint.Should().Be("changed");
    }

    [Fact]
    public async Task HostTransportSelection_ShouldUseNormalizedAliasesAndPreparedSnapshot()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        await File.WriteAllTextAsync(file.FullName, "{\"Server\":{\"Endpoint\":\"node owned-fixture.cjs\",\"Transport\":\"stdio\"}}",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var session = new CliSessionContext("configuration-transport");
        session.PrepareConfiguration(file, await BoundedArtifactReader.ReadConfigurationDocumentAsync(file));
        using var host = Program.CreateHostBuilder(["validate", "--config", file.FullName], session).Build();
        await using var scope = host.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMcpHttpClient>().Should().BeOfType<StdioMcpClientAdapter>();
        await File.WriteAllTextAsync(file.FullName, "{\"server\":{\"endpoint\":\"changed\"}}");
        var document = await session.ReadConfigurationAsync(file);

        document.Configuration.Server.Endpoint.Should().Be("node owned-fixture.cjs");
        document.Configuration.Server.Transport.Should().Be("stdio");
    }

    [Fact]
    public async Task PreparedConfiguration_ShouldRejectDifferentPathAndPreserveSnapshotOnCancellation()
    {
        Directory.CreateDirectory(_root);
        var file = new FileInfo(Path.Combine(_root, "configuration.json"));
        var other = new FileInfo(Path.Combine(_root, "other.json"));
        await File.WriteAllTextAsync(file.FullName, "{}");
        await File.WriteAllTextAsync(other.FullName, "{}");
        var prepared = await BoundedArtifactReader.ReadConfigurationDocumentAsync(file);
        var session = new CliSessionContext("configuration-binding");
        session.PrepareConfiguration(file, prepared);
        var different = () => session.ReadConfigurationAsync(other);
        await different.Should().ThrowAsync<CliUsageException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => session.ReadConfigurationAsync(file, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        (await session.ReadConfigurationAsync(file)).Should().BeSameAs(prepared);
    }

    [Fact]
    public void HostConstruction_ShouldRejectUnpreparedConfigurationWithoutOpeningIt()
    {
        var session = new CliSessionContext("configuration-admission");
        var build = () => Program.CreateHostBuilder(["validate", "--config", Path.Combine(_root, "missing.json")], session).Build();

        build.Should().Throw<CliUsageException>().WithMessage("*normalized before host construction*");
    }

    [Fact]
    public async Task ReadConfigurationDocumentAsync_ConcurrentCalls_ShouldNotShareMutableSettings()
    {
        Directory.CreateDirectory(_root);
        var documents = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            var file = new FileInfo(Path.Combine(_root, $"configuration-{index}.json"));
            await File.WriteAllTextAsync(file.FullName, $$$"""{"server":{"endpoint":"https://example.invalid/{{{index}}}"}}""");
            return await BoundedArtifactReader.ReadConfigurationDocumentAsync(file);
        }));
        documents[0].Configuration.Server.Endpoint = "changed";

        for (var index = 1; index < documents.Length; index++)
        {
            documents[index].Configuration.Server.Endpoint.Should().Be($"https://example.invalid/{index}");
            documents[index].SuppliedSettings.Should().Equal(documents[0].SuppliedSettings);
            documents[index].SuppliedSettings.Should().NotBeSameAs(documents[0].SuppliedSettings);
        }
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