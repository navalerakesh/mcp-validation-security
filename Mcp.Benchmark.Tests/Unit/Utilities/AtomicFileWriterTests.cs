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
