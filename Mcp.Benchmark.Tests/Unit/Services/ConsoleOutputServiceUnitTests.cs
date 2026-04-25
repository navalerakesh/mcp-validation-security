using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit;

/// <summary>
/// Unit tests for the ConsoleOutputService.
/// Tests professional console output formatting, color handling, and result display.
/// </summary>
[Collection("Console output")]
public class ConsoleOutputServiceUnitTests : IDisposable
{
    private readonly ConsoleOutputService _outputService;
    private readonly StringWriter _stringWriter;
    private readonly TextWriter _originalOut;
    private readonly string _sessionRoot;

    public ConsoleOutputServiceUnitTests()
    {
        // Capture console output for testing
        _stringWriter = new StringWriter();
        _originalOut = Console.Out;
        Console.SetOut(_stringWriter);

        _sessionRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var stateDir = Path.Combine(_sessionRoot, "state");
        var logsDir = Path.Combine(_sessionRoot, "logs");
        Directory.CreateDirectory(stateDir);
        Directory.CreateDirectory(logsDir);

        var sessionContext = new CliSessionContext(
            sessionId: "test-session",
            sessionRoot: _sessionRoot,
            stateDirectory: stateDir,
            logsDirectory: logsDir,
            logFilePath: Path.Combine(logsDir, "session.log"));

        _outputService = new ConsoleOutputService(sessionContext);
        _outputService.SetVerbose(true);
    }

    [Fact]
    public void WriteSuccess_ShouldFormatSuccessMessage()
    {
        // Arrange
        var message = "Operation completed successfully";

        // Act
        _outputService.WriteSuccess(message);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("✅");
        output.Should().Contain(message);
    }

    [Fact]
    public void WriteError_ShouldFormatErrorMessage()
    {
        // Arrange
        var message = "Operation failed";

        // Act
        _outputService.WriteError(message);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("❌");
        output.Should().Contain("Error:");
        output.Should().Contain(message);
    }

    [Fact]
    public void WriteWarning_ShouldFormatWarningMessage()
    {
        // Arrange
        var message = "Warning message";

        // Act
        _outputService.WriteWarning(message);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("⚠️");
        output.Should().Contain("Warning:");
        output.Should().Contain(message);
    }

    [Fact]
    public void DisplayValidationResults_WithPassedResult_ShouldShowSuccessStatus()
    {
        // Arrange
        var result = new ValidationResult
        {
            ValidationId = "test-123",
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 95.5,
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://test-server.com/mcp",
                Transport = "http"
            },
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                ComplianceScore = 100.0
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Passed,
                SecurityScore = 90.0
            },
            Recommendations = new List<string>
            {
                "Great job! Server is fully compliant."
            }
        };

        // Act
        _outputService.DisplayValidationResults(result);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("MCP SERVER VALIDATION RESULTS");
        output.Should().Contain("Status: PASSED");
        output.Should().Contain("Score:  95.5%");
        output.Should().Contain("https://test-server.com/mcp");
    }

    [Fact]
    public void DisplayValidationResults_WithFailedResult_ShouldShowFailureStatus()
    {
        // Arrange
        var result = new ValidationResult
        {
            ValidationId = "test-456",
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallStatus = ValidationStatus.Failed,
            ComplianceScore = 65.0,
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://failing-server.com/mcp",
                Transport = "http"
            },
            CriticalErrors = new List<string>
            {
                "JSON-RPC format violations detected",
                "Authentication bypass vulnerability found"
            },
            Recommendations = new List<string>
            {
                "Fix JSON-RPC compliance issues",
                "Implement proper authentication"
            }
        };

        // Act
        _outputService.DisplayValidationResults(result);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("Status: FAILED");
        output.Should().Contain("Score:  65.0%");
        output.Should().Contain("CRITICAL ISSUES");
        output.Should().Contain("JSON-RPC format violations detected");
        output.Should().Contain("RECOMMENDATIONS");
        output.Should().Contain("Fix JSON-RPC compliance issues");
    }

    [Fact]
    public void DisplayValidationResults_WithTestCategories_ShouldShowCategoryBreakdown()
    {
        // Arrange
        var result = new ValidationResult
        {
            ValidationId = "test-789",
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 88.0,
            ServerConfig = new McpServerConfig { Endpoint = "test", Transport = "http" },
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                ComplianceScore = 95.0
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Failed,
                SecurityScore = 70.0
            },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Skipped
            },
            PerformanceTesting = new PerformanceTestResult
            {
                Status = TestStatus.Passed
            }
        };

        // Act
        _outputService.DisplayValidationResults(result);

        // Assert
        var output = _stringWriter.ToString();
        output.Should().Contain("CATEGORIES");
        output.Should().Contain("Security");
        output.Should().Contain("Protocol");
        output.Should().Contain("Tools");
        output.Should().Contain("Performance");
    }

    [Fact]
    public void DisplayValidationResults_WithNullValues_ShouldHandleGracefully()
    {
        // Arrange
        var result = new ValidationResult
        {
            ValidationId = "test-null",
            OverallStatus = ValidationStatus.Error,
            ComplianceScore = 0.0,
            ServerConfig = null!,
            ProtocolCompliance = null!,
            SecurityTesting = null!,
            CriticalErrors = null!,
            Recommendations = null!
        };

        // Act & Assert - Should not throw
        Action act = () => _outputService.DisplayValidationResults(result);
        act.Should().NotThrow();
        
        var output = _stringWriter.ToString();
        output.Should().Contain("MCP SERVER VALIDATION RESULTS");
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        _stringWriter?.Dispose();
        if (!string.IsNullOrEmpty(_sessionRoot) && Directory.Exists(_sessionRoot))
        {
            Directory.Delete(_sessionRoot, recursive: true);
        }
    }
}
