using FluentAssertions;
using Mcp.Benchmark.CLI.Exceptions;

namespace Mcp.Benchmark.Tests.Unit.Cli;

public class CliExceptionTests
{
    [Fact]
    public void CliUsageException_ShouldStripAnsiSequences()
    {
        // Arrange
        const string rawMessage = "\u001b[31mInvalid\u001b[0m";

        // Act
        var exception = new CliUsageException(rawMessage);

        // Assert
        exception.Message.Should().Be("Invalid");
        exception.ExitCode.Should().Be(64);
    }

    [Fact]
    public void CliOperationException_ShouldExposeCustomExitCode()
    {
        // Act
        var exception = new CliOperationException("boom", exitCode: 99);

        // Assert
        exception.ExitCode.Should().Be(99);
    }
}
