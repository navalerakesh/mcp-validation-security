using System.Runtime.InteropServices;
using System.Text;

namespace Mcp.Benchmark.CLI.Utilities;

/// <summary>
/// Utility class for detecting and enabling terminal capabilities including emoji support.
/// </summary>
public static class TerminalCapabilities
{
    /// <summary>
    /// Initializes the console for optimal display including emoji support.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // Set console to UTF-8 encoding for emoji support
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Enable Virtual Terminal Processing on Windows for better emoji support
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnableWindowsVirtualTerminalProcessing();
            }
        }
        catch (Exception)
        {
            // Silently continue if we can't set encoding
            // This ensures the CLI still works on older systems
        }
    }

    /// <summary>
    /// Detects if the current terminal supports emoji display.
    /// </summary>
    /// <returns>True if emojis are supported, false otherwise.</returns>
    public static bool SupportsEmoji()
    {
        try
        {
            // Windows Terminal
            if (Environment.GetEnvironmentVariable("WT_SESSION") != null) return true;

            // VS Code integrated terminal
            if (Environment.GetEnvironmentVariable("VSCODE_INJECTION") != null) return true;

            // Modern terminals with good Unicode support
            var term = Environment.GetEnvironmentVariable("TERM");
            if (term?.Contains("xterm-256color") == true) return true;
            if (term?.Contains("screen-256color") == true) return true;

            // macOS Terminal (generally good emoji support)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return true;

            // Modern Linux terminals
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
                if (termProgram == "gnome-terminal" || termProgram == "konsole") return true;
            }

            // Check if we're in Windows Terminal or modern Windows console
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows Terminal sets this environment variable
                if (Environment.GetEnvironmentVariable("WT_SESSION") != null) return true;

                // Windows 10/11 with modern console features
                try
                {
                    var version = Environment.OSVersion.Version;
                    if (version.Major >= 10) return true;
                }
                catch
                {
                    // If we can't detect version, assume basic support
                }
            }

            // Default to basic support for UTF-8 capable terminals
            return Console.OutputEncoding.EncodingName.Contains("UTF");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables Virtual Terminal Processing on Windows for better Unicode and emoji support.
    /// </summary>
    private static void EnableWindowsVirtualTerminalProcessing()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (handle != IntPtr.Zero)
            {
                GetConsoleMode(handle, out uint mode);
                mode |= 0x0004; // ENABLE_VIRTUAL_TERMINAL_PROCESSING
                SetConsoleMode(handle, mode);
            }
        }
        catch
        {
            // Silently continue if VT processing can't be enabled
        }
    }

    // Windows API declarations for console manipulation
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}

/// <summary>
/// Provides emoji-aware symbols for professional CLI output.
/// </summary>
public static class CliSymbols
{
    private static readonly Lazy<bool> _supportsEmoji = new(() => TerminalCapabilities.SupportsEmoji());

    public static bool EmojiSupported => _supportsEmoji.Value;

    // Status symbols
    public static string Success => EmojiSupported ? "✅" : "[+]";
    public static string Error => EmojiSupported ? "❌" : "[!]";
    public static string Warning => EmojiSupported ? "⚠️" : "[!]";
    public static string Info => EmojiSupported ? "ℹ️" : "[i]";
    public static string Security => EmojiSupported ? "🔒" : "[#]";
    public static string Tools => EmojiSupported ? "🔧" : "[~]";
    public static string Performance => EmojiSupported ? "⚡" : "[*]";
    public static string Network => EmojiSupported ? "🌐" : "[n]";
    public static string Database => EmojiSupported ? "💾" : "[d]";
    public static string Search => EmojiSupported ? "🔍" : "[?]";
    public static string Rocket => EmojiSupported ? "🚀" : ">>";
    public static string Package => EmojiSupported ? "📦" : "[p]";
    public static string CheckMark => EmojiSupported ? "✓" : "+";
    public static string CrossMark => EmojiSupported ? "✗" : "x";
    public static string Bullet => EmojiSupported ? "•" : "-";

    // Progress and process symbols
    public static string Building => EmojiSupported ? "🔨" : "[b]";
    public static string Testing => EmojiSupported ? "🧪" : "[t]";
    public static string Validating => EmojiSupported ? "✔️" : "[v]";
    public static string Processing => EmojiSupported ? "⚙️" : "[p]";
}
