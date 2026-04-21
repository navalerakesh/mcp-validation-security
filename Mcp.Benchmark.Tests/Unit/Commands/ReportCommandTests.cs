using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.CLI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Unit.Commands;

public sealed class ReportCommandTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public async Task ExecuteAsync_ShouldGenerateFullReportByDefault()
    {
        var tempDirectory = CreateTempDirectory();
        var inputPath = Path.Combine(tempDirectory, "validation-result.json");
        var outputPath = Path.Combine(tempDirectory, "validation-report.html");

        var result = new ValidationResult
        {
            ValidationId = "report-test",
            OverallStatus = ValidationStatus.Passed,
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ValidationConfig = new McpValidatorConfiguration
            {
                Reporting = new ReportingConfig
                {
                    DetailLevel = ReportDetailLevel.Full,
                    SpecProfile = "2025-11-25"
                }
            }
        };

        await File.WriteAllTextAsync(inputPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        }));
        _tempFiles.Add(inputPath);

        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var renderer = new Mock<IValidationReportRenderer>();
        renderer
            .Setup(reportRenderer => reportRenderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>()))
            .Returns("<html></html>");

        var command = new ReportCommand(
            consoleOutput.Object,
            NullLogger<ReportCommand>.Instance,
            renderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object);

        await command.ExecuteAsync(
            new FileInfo(inputPath),
            "html",
            new FileInfo(outputPath),
            configFile: null,
            verbose: false);

        renderer.Verify(reportRenderer => reportRenderer.GenerateHtmlReport(
            It.IsAny<ValidationResult>(),
            It.IsAny<ReportingConfig>(),
            true), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithReportDetailMinimal_ShouldHonorMinimalOverride()
    {
        var tempDirectory = CreateTempDirectory();
        var inputPath = Path.Combine(tempDirectory, "validation-result.json");
        var outputPath = Path.Combine(tempDirectory, "validation-report.html");

        var result = new ValidationResult
        {
            ValidationId = "report-test",
            OverallStatus = ValidationStatus.Passed,
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ValidationConfig = new McpValidatorConfiguration
            {
                Reporting = new ReportingConfig
                {
                    DetailLevel = ReportDetailLevel.Full,
                    SpecProfile = "2025-11-25"
                }
            }
        };

        await File.WriteAllTextAsync(inputPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        }));
        _tempFiles.Add(inputPath);

        var consoleOutput = new Mock<IConsoleOutputService>(MockBehavior.Loose);
        var renderer = new Mock<IValidationReportRenderer>();
        renderer
            .Setup(reportRenderer => reportRenderer.GenerateHtmlReport(It.IsAny<ValidationResult>(), It.IsAny<ReportingConfig>(), It.IsAny<bool>()))
            .Returns("<html></html>");

        var command = new ReportCommand(
            consoleOutput.Object,
            NullLogger<ReportCommand>.Instance,
            renderer.Object,
            new Mock<IGitHubActionsReporter>(MockBehavior.Loose).Object,
            new Mock<INextStepAdvisor>(MockBehavior.Loose).Object,
            new Mock<ISessionArtifactStore>(MockBehavior.Loose).Object);

        await command.ExecuteAsync(
            new FileInfo(inputPath),
            "html",
            new FileInfo(outputPath),
            configFile: null,
            verbose: false,
            reportDetail: "minimal");

        renderer.Verify(reportRenderer => reportRenderer.GenerateHtmlReport(
            It.IsAny<ValidationResult>(),
            It.IsAny<ReportingConfig>(),
            false), Times.Once);
    }

    public void Dispose()
    {
        foreach (var tempFile in _tempFiles)
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"report-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}