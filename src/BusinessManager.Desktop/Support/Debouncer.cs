namespace BusinessManager.Desktop.Support;

/// <summary>
/// Runs an action after input has been quiet for a short delay. Each new
/// call cancels the previous one, so slow searches can never overwrite the
/// results of a newer search.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public async void Run(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(_delay, cts.Token);
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input.
        }
    }

    /// <summary>Token that is cancelled when newer input arrives.</summary>
    public CancellationToken CurrentToken => _cts?.Token ?? CancellationToken.None;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
