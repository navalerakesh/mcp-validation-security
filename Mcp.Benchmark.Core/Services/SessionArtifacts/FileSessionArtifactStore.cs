using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mcp.Benchmark.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Core.Services.SessionArtifacts;

/// <summary>
/// Persists artifacts to a local directory using JSON files. Hosts provide the directory path.
/// </summary>
public sealed class FileSessionArtifactStore : ISessionArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _stateDirectory;
    private readonly ILogger<FileSessionArtifactStore> _logger;

    public FileSessionArtifactStore(string stateDirectory, ILogger<FileSessionArtifactStore> logger)
    {
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            throw new ArgumentException("State directory must be provided", nameof(stateDirectory));
        }

        _stateDirectory = stateDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SaveJson(string artifactName, object payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_stateDirectory);
        var fileName = $"{Sanitize(artifactName)}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json";
        var path = Path.Combine(_stateDirectory, fileName);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
        _logger.LogInformation("Session artifact saved: {ArtifactPath}", path);
        return path;
    }

    public string? TrySaveJson(string artifactName, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            return SaveJson(artifactName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist session artifact {ArtifactName}", artifactName);
            return null;
        }
    }

    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "artifact";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return name.Replace(' ', '-').ToLowerInvariant();
    }
}
