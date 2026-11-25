namespace BusinessManager.Desktop.Support;

/// <summary>
/// Hands out a token per request and cancels the previous one, so when the
/// user clicks quickly only the newest request's results are applied.
/// </summary>
public sealed class LatestOnly
{
    private CancellationTokenSource? _current;

    public CancellationToken Next()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return _current.Token;
    }

    public void Cancel()
    {
        _current?.Cancel();
    }
}
