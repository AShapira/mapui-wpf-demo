namespace OgcDemo.Core;

/// <summary>UI-owned request generation. Cancellation is not enough: an old completion can still arrive.</summary>
public sealed class LatestRequest : IDisposable
{
    private CancellationTokenSource? _source;
    private long _generation;
    public (long Generation, CancellationToken Token) Begin()
    {
        _source?.Cancel(); _source?.Dispose(); _source = new CancellationTokenSource();
        return (++_generation, _source.Token);
    }
    public bool IsCurrent(long generation) => generation == _generation && _source?.IsCancellationRequested == false;
    public void Dispose() { ++_generation; _source?.Cancel(); _source?.Dispose(); _source = null; }
}
