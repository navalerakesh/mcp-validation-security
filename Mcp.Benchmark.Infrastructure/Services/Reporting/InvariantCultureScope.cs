using System.Globalization;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal sealed class InvariantCultureScope : IDisposable
{
    private static readonly CultureInfo ArtifactCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    private InvariantCultureScope()
    {
        CultureInfo.CurrentCulture = ArtifactCulture;
        CultureInfo.CurrentUICulture = ArtifactCulture;
    }

    public static InvariantCultureScope Enter() => new();

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}