using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.CLI.Models;

internal static class WindowsSupervisorProtocol
{
    internal const string Command = "__mcpval-windows-supervisor";
    internal const string Name = "mcpval.windows-supervisor";
    internal const int Version = 1;
    internal const int MaximumControlCharacters = 16 * 1024;
    internal const int MaximumArguments = 128;
    internal const int MaximumRunMilliseconds = 120_000;
    internal const int StartupTimeoutMilliseconds = 10_000;
    internal const int CleanupTimeoutMilliseconds = 5_000;
    internal const int OutputChunkBytes = 4096;
    internal const int MaximumOutputBytes = 1024 * 1024;
    internal const int PollMilliseconds = 20;

    internal static object Hello(bool supported) => new
    {
        kind = "hello", protocol = Name, protocolVersion = Version, supported,
        maximumControlCharacters = MaximumControlCharacters, maximumOutputBytes = MaximumOutputBytes
    };
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static WindowsSupervisorLaunch ParseLaunch(string json)
    {
        if (json.Length > MaximumControlCharacters) throw new InvalidDataException("Supervisor launch exceeds its control limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Supervisor launch must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Supervisor launch contains duplicate fields.");
        var launch = JsonSerializer.Deserialize<WindowsSupervisorLaunch>(json, JsonOptions)
            ?? throw new InvalidDataException("Supervisor launch is missing.");
        if (launch.ProtocolVersion != Version || launch.Arguments is not { Length: > 0 and <= MaximumArguments } ||
            launch.TimeoutMs is <= 0 or > MaximumRunMilliseconds ||
            launch.Arguments.Any(argument => argument is null || argument.Contains('\0')) ||
            launch.Arguments[0] is not ("--version" or "validate" or "health-check" or "discover"))
            throw new InvalidDataException("Supervisor launch is incompatible or outside its allowed scope.");
        return launch;
    }

    internal static async Task<string?> ReadControlLineAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length == MaximumControlCharacters) throw new InvalidDataException("Supervisor control frame exceeds its limit.");
            line.Append(character[0]);
        }
        return line.Length == 0 ? null : throw new InvalidDataException("Supervisor control frame ended before its delimiter.");
    }

    internal static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('\0'))
            throw new ArgumentException("Supervisor executable is invalid.", nameof(executable));
        var command = string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument));
        if (command.Length >= 32767) throw new InvalidDataException("Supervisor command line exceeds the Windows limit.");
        return command;
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Contains('\0')) throw new InvalidDataException("Supervisor argument contains a null character.");
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        quoted.Append('\\', backslashes * 2);
        return quoted.Append('"').ToString();
    }
}

internal sealed class WindowsSupervisorLaunch
{
    public required int ProtocolVersion { get; init; }
    public required string[] Arguments { get; init; }
    public required int TimeoutMs { get; init; }
}