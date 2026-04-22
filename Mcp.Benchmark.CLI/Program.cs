using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Mcp.Benchmark.CLI;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.CLI.Utilities.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Services.SessionArtifacts;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Authentication.Strategies;
using Mcp.Benchmark.Infrastructure.Health;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Scoring;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Infrastructure.Services.Telemetry;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Entry point for the MCP Validator (mcpval) CLI application.
/// Provides compliance, security, and performance benchmarking for MCP servers.
/// </summary>
internal class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code indicating success (0) or failure (non-zero).</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Initialize terminal capabilities for optimal display including emoji support
            TerminalCapabilities.Initialize();
            Console.OutputEncoding = Encoding.UTF8;

            // Fast path: show help/version instantly without loading DI container.
            // Covers: mcpval, mcpval -h, mcpval --help, mcpval validate -h, etc.
            var isHelpRequest = args.Length == 0 ||
                                args.Any(a => Regex.IsMatch(a, @"^--?(h(elp)?|\?+)$", RegexOptions.IgnoreCase));

            if (isHelpRequest)
            {
                // Show banner only for root help
                if (args.Length == 0 || (args.Length == 1 && isHelpRequest))
                {
                    ShowCliName();
                    ShowCliDescriptionText();
                }
                return await CreateLightweightRootCommand().Parse(args).InvokeAsync();
            }

            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine($"mcpval {GetValidatorVersion()}");
                return 0;
            }

            // Full path: build DI container only when actually running a command
            var sessionContext = McpHost.CreateSession();

            // Create and configure the host
            using var host = CreateHostBuilder(args, sessionContext).Build();

            // Create the root command and options
            var rootCommand = CreateRootCommand(host.Services);

            // Execute the command
            return await rootCommand.Parse(args).InvokeAsync();
        }
        catch (CliExceptionBase cliEx)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(cliEx.Message);
            Console.ResetColor();
            return cliEx.ExitCode;
        }
        catch (OutOfMemoryException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Fatal: Out of memory. The server response may be too large. Try reducing --max-concurrency.");
            Console.ResetColor();
            return 2;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.ResetColor();

            if (args.Contains("--verbose"))
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return 1;
        }
    }

    private static void ShowCliName()
    {
        CliTheme.ShowBanner();
    }

    private static void ShowCliDescriptionText()
    {
        CliTheme.ShowDescription();
    }

    /// <summary>
    /// Creates a lightweight root command for --help rendering only.
    /// No DI container, no service registration — just the command tree with descriptions.
    /// </summary>
    private static RootCommand CreateLightweightRootCommand()
    {
        var root = new RootCommand("MCP Validator — validate MCP servers for compliance, security, and AI safety");

        var validate = new Command("validate", "Validate an MCP server for compliance, security, and AI safety");
        validate.Options.Add(new Option<string>("--server", "-s") { Description = "MCP server endpoint (URL or STDIO command)" });
        validate.Options.Add(new Option<string>("--output", "-o") { Description = "Output directory for reports" });
        validate.Options.Add(new Option<string>("--mcpspec") { Description = "Target MCP spec profile (e.g., latest, 2025-11-25)" });
        validate.Options.Add(new Option<string>("--access") { Description = "Access intent: public, authenticated, enterprise" });
        validate.Options.Add(new Option<string>("--policy") { Description = "Validation policy mode: advisory, balanced, strict" });
        var clientProfileOption = new Option<string[]>("--client-profile", "--client-profiles") { Description = BuildClientProfileOptionDescription() };
        clientProfileOption.AllowMultipleArgumentsPerToken = true;
        validate.Options.Add(clientProfileOption);
        validate.Options.Add(new Option<string>("--token", "-t") { Description = "Bearer token for authentication" });
        validate.Options.Add(new Option<bool>("--interactive", "-i") { Description = "Allow interactive authentication" });
        validate.Options.Add(new Option<int?>("--max-concurrency") { Description = "Max in-flight HTTP requests (default: CPU count)" });

        var health = new Command("health-check", "Quick connectivity check on an MCP server");
        health.Options.Add(new Option<string>("--server", "-s") { Description = "MCP server endpoint" });

        var discover = new Command("discover", "Discover MCP server capabilities and features");
        discover.Options.Add(new Option<string>("--server", "-s") { Description = "MCP server endpoint" });

        var report = new Command("report", "Generate reports from previous validation results");
        report.Options.Add(new Option<string>("--input", "-i") { Description = "Input validation results file" });
        report.Options.Add(new Option<string>("--format", "-f") { Description = "Report format: html, xml, sarif, junit" });

        root.Subcommands.Add(validate);
        root.Subcommands.Add(health);
        root.Subcommands.Add(discover);
        root.Subcommands.Add(report);

        var verboseOpt = new Option<bool>("--verbose", "-v") { Description = "Enable verbose output", Recursive = true };
        var configOpt = new Option<string>("--config", "-c") { Description = "Path to configuration file", Recursive = true };
        var listSpecOpt = new Option<bool>("--list-spec-profiles") { Description = "List supported MCP spec profiles", Recursive = true };
        root.Options.Add(verboseOpt);
        root.Options.Add(configOpt);
        root.Options.Add(listSpecOpt);

        return root;
    }

    /// <summary>
    /// Extracts the --server / -s argument value from raw CLI args for early transport detection.
    /// </summary>
    private static string? GetServerArgFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "-s" or "--server")
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Creates and configures the dependency injection host builder.
    /// </summary>
    /// <param name="args">Command line arguments for configuration.</param>
    /// <returns>Configured host builder with all services registered.</returns>
    private static IHostBuilder CreateHostBuilder(string[] args, CliSessionContext sessionContext) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(sessionContext);
                services.AddScoped<ISessionArtifactStore>(provider =>
                {
                    var context = provider.GetRequiredService<CliSessionContext>();
                    var logger = provider.GetRequiredService<ILogger<FileSessionArtifactStore>>();
                    return new FileSessionArtifactStore(context.StateDirectory, logger);
                });

                // Register Telemetry Service (NoOp by default for CLI)
                services.AddSingleton<ITelemetryService, NoOpTelemetryService>();

                // Register HTTP or STDIO client for MCP communication
                // Detect transport from CLI args: if --server does not start with http, assume STDIO
                var serverArg = GetServerArgFromArgs(args);
                var isStdioTransport = !string.IsNullOrEmpty(serverArg) && 
                                       !serverArg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                                       !serverArg.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                if (isStdioTransport)
                {
                    services.AddSingleton<IMcpHttpClient>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<StdioMcpClientAdapter>>();
                        var adapter = new StdioMcpClientAdapter(logger);
                        try
                        {
                            adapter.StartProcessAsync(serverArg!).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to start STDIO server process: {Command}", serverArg);
                            throw new InvalidOperationException(
                                $"Failed to start STDIO MCP server '{serverArg}'. " +
                                $"Ensure the command is installed and on your PATH. Error: {ex.Message}", ex);
                        }
                        return adapter;
                    });
                }
                else
                {
                    services.AddHttpClient<McpHttpClient>(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Visual-Studio-Code/1.96.0 mcp-compliance-validator/1.0.0");
                    });
                    services.AddSingleton<IMcpHttpClient>(provider => provider.GetRequiredService<McpHttpClient>());
                }
                services.AddSingleton<IMcpClient, SdkMcpClient>();
                services.AddSingleton<IMcpClientFactory, McpClientFactory>();

                // Register professional console output service
                services.AddSingleton<IConsoleOutputService, ConsoleOutputService>();
                services.AddSingleton<IGitHubActionsReporter, GitHubActionsReporter>();
                services.AddSingleton<IReportGenerator, MarkdownReportGenerator>();
                services.AddSingleton<IValidationReportRenderer, ValidationReportRenderer>();
                services.AddSingleton<IClientProfileEvaluator, ClientProfileEvaluator>();
                services.AddScoped<INextStepAdvisor, NextStepAdvisor>();

                // Register Authentication Services
                services.AddSingleton<IAuthenticationStrategy, AzureAuthenticationStrategy>();
                services.AddSingleton<IAuthenticationStrategy, GitHubAuthenticationStrategy>();
                services.AddSingleton<IAuthenticationService, AuthenticationService>();

                // Register core services
                services.AddSingleton<ISchemaRegistry, EmbeddedSchemaRegistry>();
                services.AddSingleton<ISchemaValidator, JsonSchemaValidator>();
                services.AddSingleton<IContentSafetyAnalyzer, ContentSafetyAnalyzer>();
                services.AddSingleton<IToolAiReadinessAnalyzer, ToolAiReadinessAnalyzer>();
                services.AddSingleton<IAggregateScoringStrategy, SecurityFocusedScoringStrategy>();
                services.AddSingleton<IProtocolRuleRegistry, ProtocolRuleRegistry>();
                services.AddSingleton<IValidationSessionBuilder, ValidationSessionBuilder>();
                services.AddSingleton<IHealthCheckService, HealthCheckService>();
                services.AddSingleton<IMcpValidatorService, McpValidatorService>();
                services.AddSingleton<IProtocolComplianceValidator, ProtocolComplianceValidator>();
                services.AddSingleton<IToolValidator, ToolValidator>();
                services.AddSingleton<IResourceValidator, ResourceValidator>();
                services.AddSingleton<IPromptValidator, PromptValidator>();

                // Register Security Validator and its dependencies
                services.AddSingleton<McpCompliantAuthValidator>();
                services.AddSingleton<ISecurityValidator, SecurityValidator>();

                services.AddSingleton<IPerformanceValidator, PerformanceValidator>();

                // Register CLI command handlers
                services.AddScoped<ValidateCommand>();
                services.AddScoped<HealthCheckCommand>();
                services.AddScoped<DiscoverCommand>();
                services.AddScoped<ReportCommand>();

                // Register Report Generators
                services.AddSingleton<MarkdownReportGenerator>();

                // Configure logging
                services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddProvider(new SessionFileLoggerProvider(sessionContext));
                    // Hide HTTP client verbose logs
                    builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                    builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
                    // Hide validator service info logs (shown via console output service instead)
                    // builder.AddFilter("Mcp.Benchmark.Infrastructure", LogLevel.Warning);
                    // builder.AddFilter("Mcp.Benchmark.CLI", LogLevel.Warning);
                    builder.SetMinimumLevel(LogLevel.Information);
                });
            });

    /// <summary>
    /// Creates the root command with all available subcommands and options.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <returns>Configured root command for the CLI.</returns>
    private static RootCommand CreateRootCommand(IServiceProvider serviceProvider)
    {
        // Add global options
        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose logging output"
        };

        var configOption = new Option<FileInfo?>("--config", "-c")
        {
            Description = "Path to the configuration file"
        };

        var rootCommand = new RootCommand("MCP Validator — validate MCP servers for compliance, security, and AI safety")
        {
            CreateValidateCommand(serviceProvider, configOption, verboseOption),
            CreateHealthCheckCommand(serviceProvider, configOption, verboseOption),
            CreateDiscoverCommand(serviceProvider, configOption, verboseOption),
            CreateReportCommand(serviceProvider, configOption, verboseOption)
        };

        verboseOption.Recursive = true;
        rootCommand.Options.Add(verboseOption);
        configOption.Recursive = true;
        rootCommand.Options.Add(configOption);

        // Global option to list supported spec profiles
        var listProfilesOption = new Option<bool>("--list-spec-profiles")
        {
            Description = "List supported MCP spec profiles and exit",
            Recursive = true
        };
        rootCommand.Options.Add(listProfilesOption);

        rootCommand.SetAction((ParseResult parseResult) =>
        {
            var listProfiles = parseResult.GetValue(listProfilesOption);
            if (listProfiles)
            {
                var version = GetValidatorVersion();
                Console.WriteLine($"MCP Benchmark {version}");
                Console.WriteLine("Supported MCP spec profiles:");
                var schemaRegistry = serviceProvider.GetRequiredService<ISchemaRegistry>();
                var profiles = SpecProfileCatalog.GetProfiles(schemaRegistry);
                if (profiles.Count == 0)
                {
                    Console.WriteLine("(no embedded spec profiles detected)");
                }
                else
                {
                    foreach (var profile in profiles)
                    {
                        var aliasSuffix = profile.IsAlias && !string.IsNullOrWhiteSpace(profile.AliasOf)
                            ? $" (alias of {profile.AliasOf})"
                            : string.Empty;
                        Console.WriteLine($"- {profile.Name}{aliasSuffix}: {profile.Description}");
                    }
                }
                return;
            }

            // If no options provided, show help
            CliTheme.WriteHeader("  MCP Validator (mcpval)");
            CliTheme.WriteInfo("Use --help to see available commands and options.");
        });

        return rootCommand;
    }

    /// <summary>
    /// Creates the validate subcommand for comprehensive server validation.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="configOption">Global configuration option.</param>
    /// <param name="verboseOption">Global verbose option.</param>
    /// <returns>Configured validate command.</returns>
    private static Command CreateValidateCommand(IServiceProvider serviceProvider,
        Option<FileInfo?> configOption, Option<bool> verboseOption)
    {
        var validateCommand = new Command("validate", "Perform comprehensive MCP server validation");

        var serverOption = new Option<string>("--server", "-s")
        {
            Description = "MCP server endpoint or configuration"
        };

        var specProfileOption = new Option<string?>("--mcpspec")
        {
            Description = "Target MCP spec profile (e.g., latest, 2025-11-25, 2025-06-18)"
        };

        var serverProfileOption = new Option<string?>("--access")
        {
            Description = "Declared server access intent (public, authenticated, enterprise)"
        };
        serverProfileOption.AcceptOnlyFromAmong("public", "authenticated", "enterprise", "unspecified");

        var tokenOption = new Option<string?>("--token", "-t")
        {
            Description = "Bearer token for authentication"
        };

        var interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Allow interactive authentication (e.g. browser login) if required"
        };

        var maxConcurrencyOption = new Option<int?>("--max-concurrency")
        {
            Description = "Maximum in-flight HTTP requests the validator will issue (default: CPU count)"
        };

        var policyModeOption = new Option<string?>("--policy")
        {
            Description = "Validation policy mode (advisory, balanced, strict)",
            DefaultValueFactory = _ => Mcp.Benchmark.Core.Models.ValidationPolicyModes.Balanced
        };
        policyModeOption.AcceptOnlyFromAmong(
            Mcp.Benchmark.Core.Models.ValidationPolicyModes.Advisory,
            Mcp.Benchmark.Core.Models.ValidationPolicyModes.Balanced,
            Mcp.Benchmark.Core.Models.ValidationPolicyModes.Strict);

        var clientProfileOption = new Option<string[]>("--client-profile", "--client-profiles")
        {
            Description = BuildClientProfileOptionDescription()
        };
        clientProfileOption.AllowMultipleArgumentsPerToken = true;

        var reportDetailOption = new Option<string?>("--report-detail", "--report-mode")
        {
            Description = "Human report detail level (full, minimal). Default: full."
        };
        reportDetailOption.AcceptOnlyFromAmong("full", "minimal");


        var outputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Output directory for validation reports"
        };
            
        validateCommand.Options.Add(serverOption);
        validateCommand.Options.Add(outputOption);
        validateCommand.Options.Add(specProfileOption);
        validateCommand.Options.Add(serverProfileOption);
        validateCommand.Options.Add(tokenOption);
        validateCommand.Options.Add(interactiveOption);
        validateCommand.Options.Add(maxConcurrencyOption);
        validateCommand.Options.Add(policyModeOption);
        validateCommand.Options.Add(clientProfileOption);
        validateCommand.Options.Add(reportDetailOption);

        validateCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var specProfile = parseResult.GetValue(specProfileOption);
            var serverProfile = parseResult.GetValue(serverProfileOption);
            var config = parseResult.GetValue(configOption);
            var verbose = parseResult.GetValue(verboseOption);
            var token = parseResult.GetValue(tokenOption);
            var interactive = parseResult.GetValue(interactiveOption);
            var output = parseResult.GetValue(outputOption);
            var maxConcurrency = parseResult.GetValue(maxConcurrencyOption);
            var policyMode = parseResult.GetValue(policyModeOption);
            var clientProfiles = parseResult.GetValue(clientProfileOption);
            var reportDetail = parseResult.GetValue(reportDetailOption);

            var command = serviceProvider.GetRequiredService<ValidateCommand>();
            await command.ExecuteAsync(server!, output, specProfile, config, verbose, token, interactive, serverProfile, maxConcurrency, policyMode, clientProfiles, reportDetail);
        });

        return validateCommand;
    }

    /// <summary>
    /// Gets the validator version string from assembly metadata.
    /// </summary>
    private static string GetValidatorVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return infoVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string BuildClientProfileOptionDescription()
    {
        return $"Evaluate documented client compatibility profiles. Defaults to all supported profiles when omitted; use this option to narrow the set. Supported values: {string.Join(", ", ClientProfileCatalog.SupportedProfileIds)}, {ClientProfileCatalog.AllProfilesToken}.";
    }

    /// <summary>
    /// Creates the health-check subcommand for quick server connectivity testing.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="configOption">Global configuration option.</param>
    /// <param name="verboseOption">Global verbose option.</param>
    /// <returns>Configured health-check command.</returns>
    private static Command CreateHealthCheckCommand(IServiceProvider serviceProvider,
        Option<FileInfo?> configOption, Option<bool> verboseOption)
    {
        var healthCommand = new Command("health-check", "Perform a quick health check on the MCP server");

        var serverOption = new Option<string>("--server", "-s")
        {
            Description = "MCP server endpoint or configuration"
        };

        var timeoutOption = new Option<int>("--timeout", "-T")
        {
            Description = "Timeout for the health check in milliseconds",
            DefaultValueFactory = _ => 30000
        };

        var serverProfileOption = new Option<string?>("--access")
        {
            Description = "Declared server access intent (public, authenticated, enterprise)"
        };
        serverProfileOption.AcceptOnlyFromAmong("public", "authenticated", "enterprise", "unspecified");

        var tokenOption = new Option<string?>("--token", "-t")
        {
            Description = "Bearer token for authentication"
        };

        var interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Allow interactive authentication (e.g. browser login) if required"
        };

        healthCommand.Options.Add(serverOption);
        healthCommand.Options.Add(timeoutOption);
        healthCommand.Options.Add(serverProfileOption);
        healthCommand.Options.Add(tokenOption);
        healthCommand.Options.Add(interactiveOption);

        healthCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var config = parseResult.GetValue(configOption);
            var verbose = parseResult.GetValue(verboseOption);
            var token = parseResult.GetValue(tokenOption);
            var interactive = parseResult.GetValue(interactiveOption);
            var serverProfile = parseResult.GetValue(serverProfileOption);

            var command = serviceProvider.GetRequiredService<HealthCheckCommand>();
            await command.ExecuteAsync(server, timeout, config, verbose, token, interactive, serverProfile);
        });

        return healthCommand;
    }

    /// <summary>
    /// Creates the discover subcommand for server capability discovery.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="configOption">Global configuration option.</param>
    /// <param name="verboseOption">Global verbose option.</param>
    /// <returns>Configured discover command.</returns>
    private static Command CreateDiscoverCommand(IServiceProvider serviceProvider,
        Option<FileInfo?> configOption, Option<bool> verboseOption)
    {
        var discoverCommand = new Command("discover", "Discover MCP server capabilities and features");

        var serverOption = new Option<string>("--server", "-s")
        {
            Description = "MCP server endpoint or configuration"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Output format (json, yaml, table)",
            DefaultValueFactory = _ => "json"
        };

        var serverProfileOption = new Option<string?>("--access")
        {
            Description = "Declared server access intent (public, authenticated, enterprise)"
        };
        serverProfileOption.AcceptOnlyFromAmong("public", "authenticated", "enterprise", "unspecified");

        var tokenOption = new Option<string?>("--token", "-t")
        {
            Description = "Bearer token for authentication"
        };

        var interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Allow interactive authentication (e.g. browser login) if required"
        };

        discoverCommand.Options.Add(serverOption);
        discoverCommand.Options.Add(formatOption);
        discoverCommand.Options.Add(serverProfileOption);
        discoverCommand.Options.Add(tokenOption);
        discoverCommand.Options.Add(interactiveOption);

        discoverCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var format = parseResult.GetValue(formatOption)!;
            var config = parseResult.GetValue(configOption);
            var verbose = parseResult.GetValue(verboseOption);
            var token = parseResult.GetValue(tokenOption);
            var interactive = parseResult.GetValue(interactiveOption);
            var serverProfile = parseResult.GetValue(serverProfileOption);

            var command = serviceProvider.GetRequiredService<DiscoverCommand>();
            await command.ExecuteAsync(server, format, config, verbose, token, interactive, serverProfile);
        });

        return discoverCommand;
    }

    /// <summary>
    /// Creates the report subcommand for generating validation reports.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="configOption">Global configuration option.</param>
    /// <param name="verboseOption">Global verbose option.</param>
    /// <returns>Configured report command.</returns>
    private static Command CreateReportCommand(IServiceProvider serviceProvider,
        Option<FileInfo?> configOption, Option<bool> verboseOption)
    {
        var reportCommand = new Command("report", "Generate reports from previous validation results");

        var inputOption = new Option<FileInfo>("--input", "-i")
        {
            Description = "Input validation results file"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Report format (html, xml, sarif, junit)",
            DefaultValueFactory = _ => "html"
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "Output report file path"
        };

        var reportDetailOption = new Option<string?>("--report-detail", "--report-mode")
        {
            Description = "Human report detail level (full, minimal). Default: reuse input result, otherwise full."
        };
        reportDetailOption.AcceptOnlyFromAmong("full", "minimal");

        reportCommand.Options.Add(inputOption);
        reportCommand.Options.Add(formatOption);
        reportCommand.Options.Add(outputOption);
        reportCommand.Options.Add(reportDetailOption);

        reportCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputOption)!;
            var format = parseResult.GetValue(formatOption)!;
            var output = parseResult.GetValue(outputOption);
            var config = parseResult.GetValue(configOption);
            var verbose = parseResult.GetValue(verboseOption);
            var reportDetail = parseResult.GetValue(reportDetailOption);

            var command = serviceProvider.GetRequiredService<ReportCommand>();
            await command.ExecuteAsync(input, format, output, config, verbose, reportDetail);
        });

        return reportCommand;
    }
}
