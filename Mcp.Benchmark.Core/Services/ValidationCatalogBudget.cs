namespace Mcp.Benchmark.Core.Services;

public sealed class ValidationCatalogBudget : IDisposable
{
    private static readonly AsyncLocal<ValidationCatalogBudget?> Current = new();
    private readonly ValidationCatalogBudget? _parent;
    private long _reserved;
    private bool _disposed;
    public long Limit { get; }
    public long Reserved => Interlocked.Read(ref _reserved);

    private ValidationCatalogBudget(long limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Limit = limit;
        _parent = Current.Value;
        Current.Value = this;
    }

    public static ValidationCatalogBudget BeginRun(long limit) => new(limit);

    public static bool TryReserve(int characters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characters);
        var budget = Current.Value;
        if (budget == null) return true;
        if (budget._disposed) throw new InvalidOperationException("Catalog work continued after its run budget was disposed.");
        while (true)
        {
            var observed = Interlocked.Read(ref budget._reserved);
            if (characters > budget.Limit - observed) return false;
            if (Interlocked.CompareExchange(ref budget._reserved, observed + characters, observed) == observed) return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Current.Value = _parent;
    }
}
