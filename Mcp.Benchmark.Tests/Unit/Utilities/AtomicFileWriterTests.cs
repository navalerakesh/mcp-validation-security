using Mcp.Benchmark.CLI.Utilities;

namespace Mcp.Benchmark.Tests.Unit.Utilities;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcpval-atomic-{Guid.NewGuid():N}");

    public AtomicFileWriterTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task WriteBundleAsync_ShouldPublishAllSubjectsAtOneDirectoryBoundary()
    {
        var destination = Path.Combine(_directory, "bundle");
        var paths = await AtomicFileWriter.WriteBundleAsync(_directory, "bundle", async (staging, token) =>
        {
            var result = Path.Combine(staging, "result.json");
            await AtomicFileWriter.WriteAllTextAsync(result, "result", staging, token);
            Directory.Exists(destination).Should().BeFalse();
            var audit = Path.Combine(staging, "audit.json");
            await AtomicFileWriter.WriteAllTextAsync(audit, "audit", staging, token);
            Directory.Exists(destination).Should().BeFalse();
            return [result, audit];
        });

        paths.Should().BeEquivalentTo(Path.Combine(destination, "result.json"), Path.Combine(destination, "audit.json"));
        Directory.GetFileSystemEntries(_directory).Should().ContainSingle().Which.Should().Be(destination);
        (await File.ReadAllTextAsync(paths[0])).Should().Be("result");
        (await File.ReadAllTextAsync(paths[1])).Should().Be("audit");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("render-failure")]
    [InlineData("unlisted")]
    [InlineData("duplicate")]
    public async Task WriteBundleAsync_IncompleteSubjects_ShouldNotPublishOrLeaveStaging(string fault)
    {
        using var cancellation = new CancellationTokenSource();
        var action = () => AtomicFileWriter.WriteBundleAsync(_directory, "bundle", async (staging, token) =>
        {
            var path = Path.Combine(staging, "result.json");
            await AtomicFileWriter.WriteAllTextAsync(path, "partial", staging, token);
            if (fault == "cancel") cancellation.Cancel();
            if (fault == "render-failure") throw new IOException("Synthetic rendering failure.");
            return fault == "unlisted" ? [] : fault == "duplicate" ? new[] { path, path } : [path];
        }, cancellation.Token);

        await action.Should().ThrowAsync<Exception>();

        Directory.GetFileSystemEntries(_directory).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteBundleAsync_ExistingBundle_ShouldRemainUnchangedAfterCollision()
    {
        var destination = Path.Combine(_directory, "bundle");
        Directory.CreateDirectory(destination);
        var existing = Path.Combine(destination, "result.json");
        await File.WriteAllTextAsync(existing, "existing");
        var action = () => AtomicFileWriter.WriteBundleAsync(_directory, "bundle", async (staging, token) =>
        {
            var path = Path.Combine(staging, "result.json");
            await AtomicFileWriter.WriteAllTextAsync(path, "replacement", staging, token);
            return [path];
        });

        await action.Should().ThrowAsync<IOException>();

        (await File.ReadAllTextAsync(existing)).Should().Be("existing");
        Directory.GetFileSystemEntries(_directory).Should().ContainSingle().Which.Should().Be(destination);
    }

    [Fact]
    public async Task WriteAllTextAsync_ShouldPublishCompleteFileWithoutTemporaryResidue()
    {
        var destination = Path.Combine(_directory, "result.json");

        await AtomicFileWriter.WriteAllTextAsync(destination, "{\"complete\":true}", _directory);

        (await File.ReadAllTextAsync(destination)).Should().Be("{\"complete\":true}");
        Directory.GetFiles(_directory, "*.tmp", SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_ExistingDestination_ShouldFailWithoutOverwrite()
    {
        var destination = Path.Combine(_directory, "result.json");
        await AtomicFileWriter.WriteAllTextAsync(destination, "first", _directory);

        var action = () => AtomicFileWriter.WriteAllTextAsync(destination, "second", _directory);

        await action.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(destination)).Should().Be("first");
        Directory.GetFiles(_directory, "*.tmp", SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_PathOutsideApprovedRoot_ShouldFailBeforeCreatingFile()
    {
        var destination = Path.Combine(_directory, "..", $"outside-{Guid.NewGuid():N}.json");

        var action = () => AtomicFileWriter.WriteAllTextAsync(destination, "{}", _directory);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        File.Exists(Path.GetFullPath(destination)).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAllTextAsync_SymlinkApprovedRoot_ShouldFail()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var realRoot = Path.Combine(_directory, "real");
        var linkedRoot = Path.Combine(_directory, "linked");
        Directory.CreateDirectory(realRoot);
        Directory.CreateSymbolicLink(linkedRoot, realRoot);

        var action = () => AtomicFileWriter.WriteAllTextAsync(Path.Combine(linkedRoot, "result.json"), "{}", linkedRoot);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        File.Exists(Path.Combine(realRoot, "result.json")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
