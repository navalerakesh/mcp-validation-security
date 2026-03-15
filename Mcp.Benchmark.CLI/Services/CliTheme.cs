namespace Mcp.Benchmark.CLI.Services;

/// <summary>
/// Centralized CLI theme and styling.
/// All visual presentation — colors, banners, formatting — lives here.
/// Change this class to update the look and feel without touching any logic.
/// </summary>
public static class CliTheme
{
    // ─── Color Palette ───────────────────────────────────────────
    // Single place to adjust the entire CLI color scheme.

    public static ConsoleColor Brand       => ConsoleColor.Cyan;
    public static ConsoleColor Heading     => ConsoleColor.White;
    public static ConsoleColor Muted       => ConsoleColor.DarkGray;
    public static ConsoleColor Success     => ConsoleColor.Green;
    public static ConsoleColor Warning     => ConsoleColor.Yellow;
    public static ConsoleColor Error       => ConsoleColor.Red;
    public static ConsoleColor Accent      => ConsoleColor.Magenta;
    public static ConsoleColor Link        => ConsoleColor.Blue;
    public static ConsoleColor Command     => ConsoleColor.Green;
    public static ConsoleColor Protocol    => ConsoleColor.Yellow;
    public static ConsoleColor Security    => ConsoleColor.Red;
    public static ConsoleColor AiSafety    => ConsoleColor.Magenta;
    public static ConsoleColor Trust       => ConsoleColor.Cyan;

    private const string RepoUrl = "https://github.com/navalerakesh/mcp-validation-security";

    // ─── Banner ──────────────────────────────────────────────────

    public static void ShowBanner()
    {
        Write(Brand, @"
  ┌───────────────────────────────────────────────────────┐
  │                                                       │
  │   ███╗   ███╗ ██████╗██████╗ ██╗   ██╗ █████╗ ██╗     │
  │   ████╗ ████║██╔════╝██╔══██╗██║   ██║██╔══██╗██║     │
  │   ██╔████╔██║██║     ██████╔╝██║   ██║███████║██║     │
  │   ██║╚██╔╝██║██║     ██╔═══╝ ╚██╗ ██╔╝██╔══██║██║     │
  │   ██║ ╚═╝ ██║╚██████╗██║      ╚████╔╝ ██║  ██║█████╗  │
  │   ╚═╝     ╚═╝ ╚═════╝╚═╝       ╚═══╝  ╚═╝  ╚═╝╚════╝  │
  │                                                       │
  └───────────────────────────────────────────────────────┘
");
    }

    // ─── Description Block ───────────────────────────────────────

    public static void ShowDescription()
    {
        // Title
        WriteInline(Heading, "  MCP Validator ");
        Write(Muted, "(mcpval)");
        Console.WriteLine();

        // Tagline
        Write(Success, "  Validate MCP servers for compliance, security, and AI safety.");
        Console.WriteLine();

        // Feature tree
        WriteTreeItem("├", Protocol, "Protocol", "JSON-RPC 2.0 compliance, response structure validation");
        WriteTreeItem("├", Security, "Security", "Injection testing, auth enforcement, attack simulation");
        WriteTreeItem("├", AiSafety, "AI Safety", "LLM-friendliness grading, hallucination risk, schema quality");
        WriteTreeItem("└", Trust, "Trust", "L1-L5 trust levels (MUST/SHOULD/MAY per RFC 2119)");
        Console.WriteLine();

        // Quick start
        Write(Heading, "  Quick Start:");
        WriteCommand("mcpval validate -s https://your-server/mcp --access public");
        WriteCommand("mcpval validate -s \"npx @modelcontextprotocol/server-everything\"");
        Console.WriteLine();

        // AI agent hint
        WriteInline(Muted, "  💡 Use from AI agents: ");
        Write(Command, "npx mcpval-mcp");
        Console.WriteLine();

        // Links
        WriteLink("📌", $"{RepoUrl}#readme");
        WriteLink("📌", $"{RepoUrl}/issues");

        Write(Muted, "  ─────────────────────────────────────────────────────────────");
        Console.WriteLine();
    }

    // ─── Reusable Styled Output ──────────────────────────────────

    /// <summary>Writes a feature tree item: "  ├─ Label   Description"</summary>
    public static void WriteTreeItem(string connector, ConsoleColor labelColor, string label, string description)
    {
        WriteInline(Muted, $"  {connector}─ ");
        WriteInline(labelColor, label.PadRight(10));
        Write(Muted, description);
    }

    /// <summary>Writes a command example: "    $ command"</summary>
    public static void WriteCommand(string command)
    {
        WriteInline(Muted, "    $ ");
        Write(Command, command);
    }

    /// <summary>Writes a link: "  icon url"</summary>
    public static void WriteLink(string icon, string url)
    {
        WriteInline(Muted, $"  {icon} ");
        Write(Link, url);
    }

    /// <summary>Writes a section header.</summary>
    public static void WriteHeader(string text)
    {
        Write(Heading, text);
    }

    /// <summary>Writes a success message.</summary>
    public static void WriteSuccess(string text)
    {
        Write(Success, $"  ✅ {text}");
    }

    /// <summary>Writes a warning message.</summary>
    public static void WriteWarning(string text)
    {
        Write(Warning, $"  ⚠️  {text}");
    }

    /// <summary>Writes an error message.</summary>
    public static void WriteError(string text)
    {
        Write(Error, $"  ❌ {text}");
    }

    /// <summary>Writes an info message.</summary>
    public static void WriteInfo(string text)
    {
        Write(Muted, $"  ℹ️  {text}");
    }

    /// <summary>Writes a key-value pair: "  key: value"</summary>
    public static void WriteKeyValue(string key, string value)
    {
        WriteInline(Muted, $"  {key}: ");
        Write(Heading, value);
    }

    // ─── Low-Level Helpers ───────────────────────────────────────

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void WriteInline(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }
}
