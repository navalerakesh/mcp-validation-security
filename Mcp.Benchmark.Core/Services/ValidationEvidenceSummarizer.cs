using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationEvidenceSummarizer
{
    public static EvidenceCoverageSummary Summarize(IEnumerable<ValidationCoverageDeclaration>? coverageDeclarations)
    {
        var declarations = coverageDeclarations?.ToList() ?? new List<ValidationCoverageDeclaration>();
        if (declarations.Count == 0)
        {
            return new EvidenceCoverageSummary
            {
                TotalDeclarations = 0,
                ApplicableDeclarations = 0,
                EvidenceCoverageRatio = 0.0,
                EvidenceConfidenceRatio = 0.0,
                ConfidenceLevel = EvidenceConfidenceLevel.None
            };
        }

        var categories = declarations
            .GroupBy(declaration => string.IsNullOrWhiteSpace(declaration.LayerId) ? "unknown" : declaration.LayerId, StringComparer.OrdinalIgnoreCase)
            .Select(group => SummarizeCategory(group.Key, group.ToList()))
            .OrderBy(category => category.LayerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var applicable = CountApplicable(declarations);
        var coverageRatio = applicable == 0 ? 1.0 : declarations.Count(IsCovered) / (double)applicable;
        var confidenceRatio = applicable == 0 ? 1.0 : declarations.Sum(GetConfidenceWeight) / applicable;

        return new EvidenceCoverageSummary
        {
            TotalDeclarations = declarations.Count,
            ApplicableDeclarations = applicable,
            Covered = declarations.Count(IsCovered),
            AuthRequired = declarations.Count(IsAuthRequired),
            Inconclusive = declarations.Count(IsInconclusive),
            Skipped = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Skipped),
            NotApplicable = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.NotApplicable),
            Unavailable = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Unavailable),
            Blocked = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Blocked),
            EvidenceCoverageRatio = Math.Round(Math.Clamp(coverageRatio, 0.0, 1.0), 4),
            EvidenceConfidenceRatio = Math.Round(Math.Clamp(confidenceRatio, 0.0, 1.0), 4),
            ConfidenceLevel = ToConfidenceLevel(confidenceRatio),
            Categories = categories
        };
    }

    public static bool IsEvidenceDebt(ValidationCoverageDeclaration coverage)
    {
        return coverage.Status is not (ValidationCoverageStatus.Covered or ValidationCoverageStatus.NotApplicable);
    }

    public static bool IsCoverageBlocking(ValidationCoverageDeclaration coverage)
    {
        return coverage.Status is ValidationCoverageStatus.Blocked or ValidationCoverageStatus.Unavailable;
    }

    public static bool IsConfidenceDebt(ValidationCoverageDeclaration coverage)
    {
        if (coverage.Status != ValidationCoverageStatus.Covered)
        {
            return false;
        }

        return coverage.Confidence is EvidenceConfidenceLevel.Low or EvidenceConfidenceLevel.Medium;
    }

    private static EvidenceCoverageCategory SummarizeCategory(string layerId, List<ValidationCoverageDeclaration> declarations)
    {
        var applicable = CountApplicable(declarations);
        var coverageRatio = applicable == 0 ? 1.0 : declarations.Count(IsCovered) / (double)applicable;
        var confidenceRatio = applicable == 0 ? 1.0 : declarations.Sum(GetConfidenceWeight) / applicable;

        return new EvidenceCoverageCategory
        {
            LayerId = layerId,
            TotalDeclarations = declarations.Count,
            ApplicableDeclarations = applicable,
            Covered = declarations.Count(IsCovered),
            AuthRequired = declarations.Count(IsAuthRequired),
            Inconclusive = declarations.Count(IsInconclusive),
            Skipped = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Skipped),
            Unavailable = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Unavailable),
            Blocked = declarations.Count(declaration => declaration.Status == ValidationCoverageStatus.Blocked),
            EvidenceCoverageRatio = Math.Round(Math.Clamp(coverageRatio, 0.0, 1.0), 4),
            EvidenceConfidenceRatio = Math.Round(Math.Clamp(confidenceRatio, 0.0, 1.0), 4),
            ConfidenceLevel = ToConfidenceLevel(confidenceRatio)
        };
    }

    private static int CountApplicable(IEnumerable<ValidationCoverageDeclaration> declarations)
    {
        return declarations.Count(declaration => declaration.Status != ValidationCoverageStatus.NotApplicable);
    }

    private static bool IsCovered(ValidationCoverageDeclaration declaration)
    {
        return declaration.Status == ValidationCoverageStatus.Covered;
    }

    private static bool IsAuthRequired(ValidationCoverageDeclaration declaration)
    {
        return declaration.Status == ValidationCoverageStatus.AuthRequired || declaration.Blocker == ValidationEvidenceBlocker.AuthRequired;
    }

    private static bool IsInconclusive(ValidationCoverageDeclaration declaration)
    {
        return declaration.Status == ValidationCoverageStatus.Inconclusive;
    }

    private static double GetConfidenceWeight(ValidationCoverageDeclaration declaration)
    {
        if (declaration.Status == ValidationCoverageStatus.NotApplicable)
        {
            return 0.0;
        }

        if (declaration.Confidence != EvidenceConfidenceLevel.None)
        {
            return declaration.Confidence switch
            {
                EvidenceConfidenceLevel.High => 1.0,
                EvidenceConfidenceLevel.Medium => 0.65,
                EvidenceConfidenceLevel.Low => 0.35,
                _ => 0.0
            };
        }

        return declaration.Status switch
        {
            ValidationCoverageStatus.Covered => 1.0,
            ValidationCoverageStatus.NotApplicable => 0.0,
            ValidationCoverageStatus.Skipped => declaration.Blocker == ValidationEvidenceBlocker.ConfigDisabled ? 0.25 : 0.35,
            ValidationCoverageStatus.AuthRequired => 0.35,
            ValidationCoverageStatus.Inconclusive => 0.2,
            ValidationCoverageStatus.Unavailable => 0.0,
            ValidationCoverageStatus.Blocked => 0.0,
            _ => 0.0
        };
    }

    private static EvidenceConfidenceLevel ToConfidenceLevel(double ratio)
    {
        return ratio switch
        {
            >= 0.85 => EvidenceConfidenceLevel.High,
            >= 0.60 => EvidenceConfidenceLevel.Medium,
            > 0 => EvidenceConfidenceLevel.Low,
            _ => EvidenceConfidenceLevel.None
        };
    }
}
