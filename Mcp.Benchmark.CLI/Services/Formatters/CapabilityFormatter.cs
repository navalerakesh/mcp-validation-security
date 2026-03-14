using System.Text.Json;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class CapabilityFormatter
{
    public static void Display(ServerCapabilities capabilities, string format, bool verbose, Action<string, ConsoleColor> writeWithColor)
    {
        Console.WriteLine("Done!");
        Console.WriteLine();

        switch (format.ToLowerInvariant())
        {
            case "json":
                DisplayJson(capabilities);
                break;
            case "table":
                DisplayTable(capabilities, verbose, writeWithColor);
                break;
            case "yaml":
                DisplayYaml(capabilities);
                break;
            default:
                writeWithColor($"⚠️ Warning: Unknown format '{format}', using table format", ConsoleColor.Yellow);
                Console.WriteLine();
                DisplayTable(capabilities, verbose, writeWithColor);
                break;
        }
    }

    private static void DisplayJson(ServerCapabilities capabilities)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(capabilities, options);
        Console.WriteLine(json);
    }

    private static void DisplayTable(ServerCapabilities capabilities, bool verbose, Action<string, ConsoleColor> writeWithColor)
    {
        void WriteHeader(string title, ConsoleColor color)
        {
            writeWithColor(title, color);
            Console.WriteLine();
        }

        WriteHeader("SERVER CAPABILITIES", ConsoleColor.Green);

        Console.WriteLine($"Protocol Version: {capabilities.ProtocolVersion}");
        Console.WriteLine($"Server Name: {capabilities.Implementation.Name}");
        Console.WriteLine($"Server Version: {capabilities.Implementation.Version}");

        if (!string.IsNullOrEmpty(capabilities.Implementation.Description))
        {
            Console.WriteLine($"Description: {capabilities.Implementation.Description}");
        }

        Console.WriteLine();

        // Display supported tools
        if (capabilities.SupportedTools.Count > 0)
        {
            WriteHeader("SUPPORTED TOOLS", ConsoleColor.Yellow);
            foreach (var tool in capabilities.SupportedTools)
            {
                Console.WriteLine($"  • {tool.Name}");
                if (verbose && !string.IsNullOrEmpty(tool.Description))
                {
                    Console.WriteLine($"    {tool.Description}");
                }
            }
            Console.WriteLine();
        }

        // Display supported resources
        if (capabilities.SupportedResources.Count > 0)
        {
            WriteHeader("SUPPORTED RESOURCES", ConsoleColor.Yellow);
            foreach (var resource in capabilities.SupportedResources)
            {
                Console.WriteLine($"  • {resource.UriPattern}");
                if (verbose && !string.IsNullOrEmpty(resource.Description))
                {
                    Console.WriteLine($"    {resource.Description}");
                }
            }
            Console.WriteLine();
        }

        // Display supported prompts
        if (capabilities.SupportedPrompts.Count > 0)
        {
            WriteHeader("SUPPORTED PROMPTS", ConsoleColor.Yellow);
            foreach (var prompt in capabilities.SupportedPrompts)
            {
                Console.WriteLine($"  • {prompt.Name}");
                if (verbose && !string.IsNullOrEmpty(prompt.Description))
                {
                    Console.WriteLine($"    {prompt.Description}");
                }
            }
            Console.WriteLine();
        }

        // Display transport support
        if (capabilities.SupportedTransports.Count > 0)
        {
            WriteHeader("SUPPORTED TRANSPORTS", ConsoleColor.Yellow);
            foreach (var transport in capabilities.SupportedTransports)
            {
                Console.WriteLine($"  • {transport}");
            }
            Console.WriteLine();
        }
    }

    private static void DisplayYaml(ServerCapabilities capabilities)
    {
        // Simple YAML-like output
        Console.WriteLine("protocolVersion: " + capabilities.ProtocolVersion);
        Console.WriteLine("implementation:");
        Console.WriteLine($"  name: {capabilities.Implementation.Name}");
        Console.WriteLine($"  version: {capabilities.Implementation.Version}");

        if (!string.IsNullOrEmpty(capabilities.Implementation.Description))
        {
            Console.WriteLine($"  description: \"{capabilities.Implementation.Description}\"");
        }

        if (capabilities.SupportedTools.Count > 0)
        {
            Console.WriteLine("supportedTools:");
            foreach (var tool in capabilities.SupportedTools)
            {
                Console.WriteLine($"  - name: {tool.Name}");
                if (!string.IsNullOrEmpty(tool.Description))
                {
                    Console.WriteLine($"    description: \"{tool.Description}\"");
                }
            }
        }

        if (capabilities.SupportedResources.Count > 0)
        {
            Console.WriteLine("supportedResources:");
            foreach (var resource in capabilities.SupportedResources)
            {
                Console.WriteLine($"  - uriPattern: {resource.UriPattern}");
                if (!string.IsNullOrEmpty(resource.Description))
                {
                    Console.WriteLine($"    description: \"{resource.Description}\"");
                }
            }
        }

        if (capabilities.SupportedTransports.Count > 0)
        {
            Console.WriteLine("supportedTransports:");
            foreach (var transport in capabilities.SupportedTransports)
            {
                Console.WriteLine($"  - {transport}");
            }
        }
    }
}
