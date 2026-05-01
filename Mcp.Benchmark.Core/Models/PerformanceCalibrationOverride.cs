namespace Mcp.Benchmark.Core.Models;

public sealed class PerformanceCalibrationOverride
{
    public string RuleId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Recommendation { get; set; } = string.Empty;

    public List<string> AffectedTests { get; set; } = new();

    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TestStatus BeforeStatus { get; set; }

    public TestStatus AfterStatus { get; set; }

    public double BeforeScore { get; set; }

    public double AfterScore { get; set; }

    public ValidationFindingSeverity BeforeSeverity { get; set; }

    public ValidationFindingSeverity AfterSeverity { get; set; }

    public bool ChangedComponentStatus { get; set; }

    public bool ChangedDeterministicVerdict { get; set; }
}
