using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public sealed class ReportBudgetProjectionTests
{
    [Theory]
    [InlineData(0L, 100L, "0/100 native catalog payload characters reserved; not whole-process memory")]
    [InlineData(100L, 100L, "100/100 native catalog payload characters reserved; not whole-process memory")]
    [InlineData(null, null, "Native catalog payload budget not recorded")]
    [InlineData(101L, 100L, "Native catalog payload budget is invalid")]
    [InlineData(1L, null, "Native catalog payload budget is invalid")]
    [InlineData(null, 100L, "Native catalog payload budget is invalid")]
    public void CatalogBudget_ShouldUseOneProjectionWithoutInventingHistoricalMeasurement(long? reserved, long? limit, string expected)
    {
        var result = new ValidationResult();
        result.Run.OperationalMetrics = new() { CatalogPayloadCharactersReserved = reserved, CatalogPayloadCharacterLimit = limit };
        ReportContextFormatter.FormatCatalogBudget(result.Run.OperationalMetrics).Should().Be(expected);
        var clone = result.CloneWithoutSecrets();
        clone.Run.OperationalMetrics!.CatalogPayloadCharactersReserved.Should().Be(reserved);
        if (limit != null || reserved != null)
        {
            new MarkdownReportGenerator().GenerateReport(clone).Should().Contain(expected);
            new ValidationReportRenderer().GenerateHtmlReport(clone, new ReportingConfig(), verbose: true).Should().Contain(expected);
        }
    }
}
