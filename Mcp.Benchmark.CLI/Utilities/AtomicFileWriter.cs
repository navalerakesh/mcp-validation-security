using System.Text;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class AtomicFileWriter
{
    public static async Task<IReadOnlyList<string>> WriteBundleAsync(
        string approvedRoot,
        string bundleName,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> writeContents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleName);
        ArgumentNullException.ThrowIfNull(writeContents);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(approvedRoot);
        Directory.CreateDirectory(root);
        var destination = Path.GetFullPath(Path.Combine(root, bundleName));
        EnsureApprovedDirectChild(root, destination);
        var staging = Path.Combine(root, $".pending-{Guid.NewGuid():N}");
        var committed = false;
        try
        {
            Directory.CreateDirectory(staging);
            var stagedPaths = await writeContents(staging, cancellationToken).ConfigureAwait(false);
            var expected = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var path in stagedPaths)
            {
                var fullPath = Path.GetFullPath(path);
                EnsureApprovedDirectChild(staging, fullPath);
                if (!File.Exists(fullPath) || !expected.Add(fullPath))
                    throw new InvalidOperationException("Artifact bundle contains missing or duplicate subjects.");
            }
            if (expected.Count == 0 || !expected.SetEquals(Directory.GetFiles(staging)) || Directory.GetDirectories(staging).Length != 0)
                throw new InvalidOperationException("Artifact bundle subjects do not match the staged files.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureApprovedDirectChild(root, destination);
            Directory.Move(staging, destination);
            committed = true;
            return stagedPaths.Select(path => Path.Combine(destination, Path.GetFileName(path))).ToArray();
        }
        finally
        {
            if (!committed && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static async Task WriteAllTextAsync(
        string destinationPath,
        string content,
        string? approvedRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(destinationPath);
        string? fullRoot = null;
        if (!string.IsNullOrWhiteSpace(approvedRoot))
        {
            fullRoot = Path.GetFullPath(approvedRoot);
            EnsureApprovedDirectChild(fullRoot, fullPath);
        }
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Artifact destination must have a parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            if (fullRoot != null)
            {
                EnsureApprovedDirectChild(fullRoot, fullPath);
            }
            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureApprovedDirectChild(string fullRoot, string fullPath)
    {
        var rootInfo = new DirectoryInfo(fullRoot);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Approved output root does not exist: {fullRoot}");
        }
        if (rootInfo.LinkTarget != null || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnauthorizedAccessException("Approved output root cannot be a symbolic link or reparse point.");
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(parent ?? string.Empty),
                Path.TrimEndingDirectorySeparator(fullRoot),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Artifact destination must be a direct child of the approved output root.");
        }
    }
}
