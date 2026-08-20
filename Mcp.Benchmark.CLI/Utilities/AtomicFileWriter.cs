using System.Text;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class AtomicFileWriter
{
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
