namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class FormatterUtils
{
    public static void WriteWithColor(string text, ConsoleColor color, bool useColors = true)
    {
        if (useColors)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = originalColor;
        }
        else
        {
            Console.Write(text);
        }
    }

    public static void WriteLineWithColor(string text, ConsoleColor color, bool useColors = true)
    {
        WriteWithColor(text, color, useColors);
        Console.WriteLine();
    }

    public static void DisplaySectionHeader(string title, ConsoleColor color, bool useColors = true)
    {
        Console.WriteLine();
        WriteLineWithColor(title.ToUpperInvariant(), color, useColors);
        WriteLineWithColor(new string('-', title.Length), ConsoleColor.DarkGray, useColors);
    }

    public static void DisplaySubsectionHeader(string title, ConsoleColor color, bool useColors = true)
    {
        Console.WriteLine();
        WriteLineWithColor(title, color, useColors);
    }

    public static string TruncateAndPad(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "".PadRight(maxLength);

        if (text.Length <= maxLength)
            return text.PadRight(maxLength);

        return text.Substring(0, maxLength - 3) + "...";
    }

    public static ConsoleColor GetScoreColor(double score)
    {
        return score switch
        {
            >= 95 => ConsoleColor.Green,
            >= 85 => ConsoleColor.Cyan,
            >= 75 => ConsoleColor.Yellow,
            >= 65 => ConsoleColor.Magenta,
            _ => ConsoleColor.Red
        };
    }
}
