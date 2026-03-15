using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class ToolFormatter
{
    public static void DisplayBreakdown(ValidationResult result, bool useColors)
    {
        if (result.ToolValidation == null) return;

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("TOOL VALIDATION", ConsoleColor.Cyan, useColors);
        Console.WriteLine(new string('-', 40));

        var tools = result.ToolValidation;
        Console.WriteLine($"Score: {tools.Score:F1}%");
        Console.WriteLine($"Tools Discovered: {tools.ToolsDiscovered}");

        if (tools.DiscoveredToolNames.Any())
        {
            var displayCount = Math.Min(tools.DiscoveredToolNames.Count, 15);
            var toolsToDisplay = tools.DiscoveredToolNames.Take(displayCount);

            Console.WriteLine();
            FormatterUtils.WriteLineWithColor("DISCOVERED TOOLS", ConsoleColor.White, useColors);
            foreach (var toolName in toolsToDisplay)
            {
                Console.WriteLine($"  - {toolName}");
            }

            if (tools.DiscoveredToolNames.Count > displayCount)
            {
                FormatterUtils.WriteLineWithColor($"  ... and {tools.DiscoveredToolNames.Count - displayCount} more", ConsoleColor.Gray, useColors);
            }
            Console.WriteLine();
        }

        if (tools.ToolsTestPassed > 0) FormatterUtils.WriteLineWithColor($"Passed: {tools.ToolsTestPassed}", ConsoleColor.Green, useColors);
        if (tools.ToolsTestFailed > 0) FormatterUtils.WriteLineWithColor($"Failed: {tools.ToolsTestFailed}", ConsoleColor.Red, useColors);

        if (tools.ToolResults.Any(t => t.Status == TestStatus.Failed))
        {
            DisplayFailureTable(tools.ToolResults.Where(t => t.Status == TestStatus.Failed), 0, useColors);
        }
        else if (tools.Status == TestStatus.Passed && tools.Score > 0)
        {
            // Check for Auth Check result with header
            var authCheck = tools.ToolResults.FirstOrDefault(t => !string.IsNullOrEmpty(t.WwwAuthenticateHeader));
            if (authCheck != null)
            {
                Console.WriteLine();
                FormatterUtils.WriteLineWithColor("AUTHENTICATION ENFORCED", ConsoleColor.Yellow, useColors);
                Console.WriteLine($"  Method: {authCheck.WwwAuthenticateHeader}");

                if (authCheck.AuthMetadata != null)
                {
                    Console.WriteLine();
                    FormatterUtils.WriteLineWithColor("  OAUTH 2.0 METADATA DISCOVERED", ConsoleColor.Cyan, useColors);
                    if (authCheck.AuthMetadata.AuthorizationServers?.Any() == true)
                        Console.WriteLine($"  Authority: {authCheck.AuthMetadata.AuthorizationServers.First()}");

                    if (authCheck.AuthMetadata.ScopesSupported?.Any() == true)
                        Console.WriteLine($"  Scopes:    {string.Join(", ", authCheck.AuthMetadata.ScopesSupported)}");

                    // Azure AD Hint
                    if (authCheck.AuthMetadata.AuthorizationServers?.Any(s => s.Contains("login.microsoftonline.com")) == true &&
                        authCheck.AuthMetadata.ScopesSupported?.Any() == true)
                    {
                        Console.WriteLine();
                        FormatterUtils.WriteLineWithColor("  ℹ️  To authenticate with this server, run:", ConsoleColor.White, useColors);
                        Console.WriteLine($"      az account get-access-token --scope \"{authCheck.AuthMetadata.ScopesSupported.First()}\" --query accessToken -o tsv");
                    }
                }
                Console.WriteLine();
            }

            FormatterUtils.WriteLineWithColor("✅ All tool validations passed", ConsoleColor.Green, useColors);
        }
    }

    public static void DisplayFailureTable(IEnumerable<IndividualToolResult> failedTools, int indent, bool useColors)
    {
        var indentString = new string(' ', indent);

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor($"{indentString}TOOL FAILURES", ConsoleColor.Red, useColors);
        Console.WriteLine($"{indentString}{new string('-', 30)}");

        foreach (var tool in failedTools.Take(10))
        {
            var failureType = GetToolFailureType(tool);
            var details = tool.Issues.Any()
                ? FormatterUtils.TruncateAndPad(tool.Issues.First(), 50)
                : "No details available";

            Console.Write(indentString);
            FormatterUtils.WriteWithColor(tool.ToolName.PadRight(20), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(failureType.PadRight(15), ConsoleColor.Yellow, useColors);
            FormatterUtils.WriteLineWithColor(details, ConsoleColor.Gray, useColors);
        }

        if (failedTools.Count() > 10)
        {
            FormatterUtils.WriteWithColor($"{indentString}... and {failedTools.Count() - 10} more failed tools", ConsoleColor.Gray, useColors);
            Console.WriteLine();
        }
    }

    private static string GetToolFailureType(IndividualToolResult tool)
    {
        if (!tool.DiscoveredCorrectly) return "DISCOVERY";
        if (!tool.MetadataValid) return "METADATA";
        if (!tool.ExecutionSuccessful) return "EXECUTION";
        if (tool.ParameterTests.Any(p => !p.ValidationPassed)) return "PARAMETERS";
        return "UNKNOWN";
    }
}
