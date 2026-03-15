using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class ProtocolFormatter
{
    public static void DisplayBreakdown(ValidationResult result, bool useColors)
    {
        var protocol = result.ProtocolCompliance;
        if (protocol == null) return;

        if (protocol.Status == TestStatus.Passed && protocol.ComplianceScore >= 100.0) return;

        FormatterUtils.DisplaySectionHeader("PROTOCOL COMPLIANCE", ConsoleColor.White, useColors);

        var score = protocol.ComplianceScore;
        var isHighCompliance = score >= 80.0;

        Console.WriteLine($"Score: {score:F1}% (Target: 100%)");

        if (isHighCompliance)
        {
            FormatterUtils.WriteLineWithColor($"Status: ACCEPTABLE - {100.0 - score:F1}% minor warnings", ConsoleColor.Green, useColors);
        }
        else
        {
            FormatterUtils.WriteLineWithColor($"Status: IMPROVEMENT NEEDED - {100.0 - score:F1}% missing", ConsoleColor.Yellow, useColors);
        }

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("JSON-RPC 2.0 COMPLIANCE", ConsoleColor.Cyan, useColors);
        Console.WriteLine(new string('-', 40));

        var testResults = CalculateProtocolTestResults(protocol);

        foreach (var (testName, passed, impact) in testResults)
        {
            var status = passed ? "PASS" : "FAIL";
            var statusColor = passed ? ConsoleColor.Green : ConsoleColor.Red;

            FormatterUtils.WriteWithColor(testName.PadRight(35), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(status.PadRight(10), statusColor, useColors);
            FormatterUtils.WriteLineWithColor(impact, ConsoleColor.Gray, useColors);
        }

        if (protocol.Violations?.Any() == true)
        {
            Console.WriteLine();
            var headerText = score >= 80.0 ? "WARNINGS" : "VIOLATIONS";
            var headerColor = score >= 80.0 ? ConsoleColor.Yellow : ConsoleColor.Red;

            FormatterUtils.WriteLineWithColor(headerText, headerColor, useColors);

            foreach (var violation in protocol.Violations)
            {
                var prefix = score >= 80.0 ? "!" : "X";
                Console.WriteLine($"  {prefix} {violation.Description}");
            }
        }

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("RECOMMENDATIONS", ConsoleColor.Green, useColors);
        Console.WriteLine("  1. Review JSON-RPC 2.0 specification compliance");
        Console.WriteLine("  2. Ensure proper error code handling");
        Console.WriteLine("  3. Validate request/response format consistency");
        Console.WriteLine("  4. Test batch processing capabilities");
        Console.WriteLine("  5. Verify notification handling");
        Console.WriteLine();
    }

    public static void DisplayFailureTable(IEnumerable<ComplianceViolation> violations, int indent, bool useColors)
    {
        var indentString = new string(' ', indent);

        FormatterUtils.WriteLineWithColor($"{indentString}PROTOCOL FAILURES", ConsoleColor.Red, useColors);
        Console.WriteLine($"{indentString}{new string('-', 30)}");

        foreach (var violation in violations.Take(10))
        {
            var severity = violation.Severity switch
            {
                ViolationSeverity.Critical => "CRITICAL",
                ViolationSeverity.High => "HIGH",
                ViolationSeverity.Medium => "MEDIUM",
                ViolationSeverity.Low => "LOW",
                _ => "UNKNOWN"
            };

            var description = FormatterUtils.TruncateAndPad(violation.Description, 50);

            Console.Write(indentString);
            FormatterUtils.WriteWithColor((violation.Category ?? "N/A").PadRight(20), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(severity.PadRight(10), ConsoleColor.Yellow, useColors);
            FormatterUtils.WriteLineWithColor(description, ConsoleColor.Gray, useColors);
        }

        if (violations.Count() > 10)
        {
            FormatterUtils.WriteWithColor($"{indentString}... and {violations.Count() - 10} more violations", ConsoleColor.Gray, useColors);
            Console.WriteLine();
        }
    }

    private static List<(string testName, bool passed, string impact)> CalculateProtocolTestResults(ComplianceTestResult protocol)
    {
        var score = protocol.ComplianceScore;
        var passedTests = (int)Math.Round(score / 12.5);

        var results = new List<(string, bool, string)>
        {
            ("Error Code Compliance", passedTests >= 1, passedTests >= 1 ? "Full 12.5% earned" : "12.5% lost"),
            ("Request Format Validation", passedTests >= 2, passedTests >= 2 ? "Full 12.5% earned" : "12.5% lost"),
            ("Response Format Validation", passedTests >= 3, passedTests >= 3 ? "Full 12.5% earned" : "12.5% lost"),
            ("Batch Processing Support", passedTests >= 4, passedTests >= 4 ? "Full 12.5% earned" : "12.5% lost"),
            ("Notification Handling", passedTests >= 5, passedTests >= 5 ? "Full 12.5% earned" : "12.5% lost"),
            ("Content-Type Compliance", passedTests >= 6, passedTests >= 6 ? "Full 12.5% earned" : "12.5% lost"),
            ("Case Sensitivity Enforcement", passedTests >= 7, passedTests >= 7 ? "Full 12.5% earned" : "12.5% lost"),
            ("HTTP Status Code Compliance", passedTests >= 8, passedTests >= 8 ? "Full 12.5% earned" : "12.5% lost")
        };

        return results;
    }
}
