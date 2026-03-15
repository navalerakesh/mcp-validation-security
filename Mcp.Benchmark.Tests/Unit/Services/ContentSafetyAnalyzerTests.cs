using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ContentSafetyAnalyzerTests
{
    private readonly ContentSafetyAnalyzer _analyzer;

    public ContentSafetyAnalyzerTests()
    {
        _analyzer = new ContentSafetyAnalyzer(new Mock<ILogger<ContentSafetyAnalyzer>>().Object);
    }

    [Fact]
    public void AnalyzeTool_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzeTool("get_weather");
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeTool_WithNullName_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzeTool(null!);
        findings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeResource_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzeResource("doc.txt", "file:///doc.txt");
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeResource_WithNullValues_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzeResource(null!, null!);
        findings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzePrompt_WithSafeName_ShouldReturnEmpty()
    {
        var findings = _analyzer.AnalyzePrompt("code_review", "Reviews code quality", 2);
        findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzePrompt_WithNullValues_ShouldNotThrow()
    {
        var findings = _analyzer.AnalyzePrompt(null!, null, 0);
        findings.Should().NotBeNull();
    }
}
