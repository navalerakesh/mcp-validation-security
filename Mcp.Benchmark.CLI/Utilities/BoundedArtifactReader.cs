using System.Text;
using System.Text.Json;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Utilities;

internal static class BoundedArtifactReader
{
    internal const int MaximumValidationResultBytes = 16 * 1024 * 1024;
    internal const int MaximumConfigurationBytes = 1024 * 1024;

    internal static async Task<ValidationResult> ReadValidationResultAsync(
        FileInfo file,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadTextAsync(file, MaximumValidationResultBytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 128,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("documentType", out var documentType) || documentType.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("documentSchemaVersion", out var schemaVersion) || schemaVersion.ValueKind != JsonValueKind.String)
        {
            throw new CliUsageException("Validation artifact is missing its document type or schema version.");
        }

        var type = documentType.GetString();
        var version = schemaVersion.GetString();
        if (!string.Equals(type, ArtifactContracts.ValidationResultDocumentType, StringComparison.Ordinal) ||
            !ArtifactContracts.IsCompatible(type!, version!))
        {
            throw new CliUsageException($"Validation artifact contract '{type}' version '{version}' is not compatible with this validator.");
        }

        return JsonSerializer.Deserialize<ValidationResult>(
            json,
            ArtifactJsonOptions.Create(writeIndented: false, propertyNameCaseInsensitive: true))
            ?? throw new CliUsageException("Validation artifact did not contain a validation result.");
    }

    internal static async Task<string> ReadTextAsync(
        FileInfo file,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) throw new CliUsageException($"Input file was not found: {file.FullName}");
        if (file.LinkTarget != null) throw new CliUsageException("Input files must be regular files, not symbolic links.");
        if (file.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        var bytes = new byte[maximumBytes + 1];
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        if (totalRead > maximumBytes || stream.ReadByte() != -1)
        {
            throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        }
        return new UTF8Encoding(false, true).GetString(bytes, 0, totalRead);
    }

    internal static string ReadText(FileInfo file, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) throw new CliUsageException($"Input file was not found: {file.FullName}");
        if (file.LinkTarget != null) throw new CliUsageException("Input files must be regular files, not symbolic links.");
        if (file.Length > maximumBytes) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var bytes = new byte[maximumBytes + 1];
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
            if (read == 0) break;
            totalRead += read;
        }
        if (totalRead > maximumBytes || stream.ReadByte() != -1) throw new CliUsageException($"Input file exceeds the {maximumBytes}-byte limit.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, totalRead);
    }
}