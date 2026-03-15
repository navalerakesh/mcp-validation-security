using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class SecurityFormatter
{
    public static void DisplayAssessmentBreakdown(ValidationResult result, bool useColors)
    {
        if (result.SecurityTesting == null) return;

        FormatterUtils.DisplaySectionHeader("SECURITY ASSESSMENT", ConsoleColor.White, useColors);

        var security = result.SecurityTesting;

        FormatterUtils.DisplaySubsectionHeader("Authentication Testing", ConsoleColor.Cyan, useColors);
        Console.WriteLine($"Score: {security.AuthenticationTestResult?.ComplianceScore ?? 0:F1}% ({GetPassedAuthScenarios(security)}/{GetTotalAuthScenarios(security)} passed)");
        Console.WriteLine($"Time:  {security.Duration.TotalSeconds:F2}s");

        // Display authentication table
        DisplayAuthenticationTestingTable(security, useColors);

        // Display attack simulation if available
        if (security?.AttackSimulations?.Any() == true)
        {
            DisplayAttackSimulationTable(security.AttackSimulations, useColors);
        }
    }

    private static void DisplayAuthenticationTestingTable(SecurityTestResult security, bool useColors)
    {
        Console.WriteLine();
        var header = string.Format("{0,-30} {1,-20} {2,-30} {3,-20} {4}",
            "SCENARIO", "METHOD", "EXPECTED", "ACTUAL", "ANALYSIS");

        FormatterUtils.WriteLineWithColor(header, ConsoleColor.DarkGray, useColors);
        Console.WriteLine(new string('-', header.Length));

        if (security.AuthenticationTestResult?.TestScenarios?.Any() == true)
        {
            var sortedScenarios = security.AuthenticationTestResult.TestScenarios
                .OrderBy(s => s.TestType switch
                {
                    "No Auth" => 0,
                    "Malformed Token" => 1,
                    "Invalid Token" => 2,
                    "Token Expired" => 3,
                    "Valid Token" => 4,
                    _ => 5
                })
                .ThenBy(s => s.Method)
                .ToList();

            foreach (var scenario in sortedScenarios)
            {
                var (expected, serverResponse) = scenario.TestType switch
                {
                    "No Auth" => ("400/401 + WWW-Auth", GetServerResponseText(scenario)),
                    "Malformed Token" => ("400/401 + WWW-Auth", GetServerResponseText(scenario)),
                    "Invalid Token" => ("400/401 + WWW-Auth", GetServerResponseText(scenario)),
                    "Token Expired" => ("400/401 + WWW-Auth", GetServerResponseText(scenario)),
                    "Valid Token" => ("200 OK + Success Response", GetServerResponseText(scenario)),
                    _ => ("Check Protocol Spec", GetServerResponseText(scenario))
                };

                var expectedText = FormatterUtils.TruncateAndPad(expected, 28);
                var serverResponseText = FormatterUtils.TruncateAndPad(serverResponse, 18);
                var analysis = scenario.Analysis ?? "Pending";

                var cleanScenarioName = FormatterUtils.TruncateAndPad(scenario.TestType ?? "Unknown", 23);
                var methodName = FormatterUtils.TruncateAndPad(scenario.Method, 18);
                var analysisText = FormatterUtils.TruncateAndPad(analysis, 40);

                var analysisColor = analysis.Contains("✅") || analysis.Contains("COMPLIANT") ? ConsoleColor.Green :
                                   analysis.Contains("❌") || analysis.Contains("VIOLATION") ? ConsoleColor.Red :
                                   analysis.Contains("⚠️") || analysis.Contains("PRACTICAL") ? ConsoleColor.Yellow : ConsoleColor.Gray;

                var serverResponseColor = scenario.IsCompliant ? ConsoleColor.Green : ConsoleColor.Red;

                FormatterUtils.WriteWithColor(cleanScenarioName.PadRight(25), ConsoleColor.White, useColors);
                FormatterUtils.WriteWithColor(methodName.PadRight(20), ConsoleColor.Cyan, useColors);
                FormatterUtils.WriteWithColor(expectedText.PadRight(30), ConsoleColor.Cyan, useColors);
                FormatterUtils.WriteWithColor(serverResponseText.PadRight(20), serverResponseColor, useColors);
                FormatterUtils.WriteLineWithColor(analysisText, analysisColor, useColors);

                if (!string.IsNullOrEmpty(scenario.WwwAuthenticateHeader))
                {
                    FormatterUtils.WriteLineWithColor($"   ↳ Auth Method: {scenario.WwwAuthenticateHeader}", ConsoleColor.DarkGray, useColors);
                }
            }
        }
        else
        {
            FormatterUtils.WriteLineWithColor("No authentication scenarios run.", ConsoleColor.Yellow, useColors);
        }
    }

    private static void DisplayAttackSimulationTable(List<AttackSimulationResult> attacks, bool useColors)
    {
        FormatterUtils.DisplaySubsectionHeader("Attack Simulation", ConsoleColor.Magenta, useColors);
        Console.WriteLine($"Vectors: {attacks.Count} | Avg Time: {attacks.Average(a => a.ExecutionTimeMs):F0}ms");
        Console.WriteLine();

        var header = string.Format("{0,-30} {1,-50} {2,-15} {3}", "THREAT VECTOR", "DESCRIPTION", "RESULT", "ANALYSIS");
        FormatterUtils.WriteLineWithColor(header, ConsoleColor.DarkGray, useColors);
        Console.WriteLine(new string('-', header.Length));

        // Show all recorded attack simulations so operators can
        // correlate every executed vector with its outcome. The
        // list is typically small and this avoids hiding details.
        foreach (var attack in attacks)
        {
            var (result, resultColor) = attack.AttackSuccessful ? ("DETECTED", ConsoleColor.Red) : ("BLOCKED", ConsoleColor.Green);

            var analysis = "System-level protection";
            if (attack.Evidence != null && attack.Evidence.TryGetValue("defenseMechanism", out var mechanismObj))
            {
                analysis = mechanismObj?.ToString() ?? "System-level protection";
            }
            else if (attack.DefenseSuccessful)
            {
                analysis = "Authentication-first defense";
            }

            FormatterUtils.WriteWithColor(attack.AttackVector.PadRight(30), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(FormatterUtils.TruncateAndPad(attack.Description, 38).PadRight(40), ConsoleColor.Gray, useColors);
            FormatterUtils.WriteWithColor(result.PadRight(15), resultColor, useColors);
            FormatterUtils.WriteLineWithColor(analysis, ConsoleColor.Gray, useColors);
        }
    }

    private static string GetServerResponseText(AuthenticationScenario scenario)
    {
        var statusCode = scenario.StatusCode ?? "Unknown";
        var hasWwwAuth = scenario.Analysis?.Contains("WWW-Authenticate") == true || scenario.Analysis?.Contains("COMPLIANT") == true;
        var hasActualJsonResponse = scenario.ActualBehavior?.Contains("JSON-RPC Error") == true;

        return scenario.TestType switch
        {
            "No Auth" => $"{statusCode}{(hasActualJsonResponse ? " + JSON-RPC Error" : hasWwwAuth ? " + WWW-Auth" : "")}",
            "Malformed Token" => $"{statusCode}{(hasActualJsonResponse ? " + JSON-RPC Error" : hasWwwAuth ? " + WWW-Auth" : "")}",
            "Invalid Token" => $"{statusCode}{(hasActualJsonResponse ? " + JSON-RPC Error" : hasWwwAuth ? " + WWW-Auth" : "")}",
            "Token Expired" => $"{statusCode}{(hasActualJsonResponse ? " + JSON-RPC Error" : hasWwwAuth ? " + WWW-Auth" : "")}",
            "Valid Token" => $"{statusCode} + Response",
            _ => statusCode
        };
    }

    private static int GetPassedAuthScenarios(SecurityTestResult security)
    {
        return security.AuthenticationTestResult?.TestScenarios?.Count(s => s.IsCompliant) ?? 0;
    }

    private static int GetTotalAuthScenarios(SecurityTestResult security)
    {
        return security.AuthenticationTestResult?.TestScenarios?.Count ?? 0;
    }
}
