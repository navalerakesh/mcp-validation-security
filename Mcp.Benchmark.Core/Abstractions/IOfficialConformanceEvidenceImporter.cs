using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public interface IOfficialConformanceEvidenceImporter
{
    OfficialConformanceImportResult Import(string json, OfficialConformanceImportContext? context = null);
}