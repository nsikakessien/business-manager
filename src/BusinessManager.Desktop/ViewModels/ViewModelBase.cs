using BusinessManager.Application.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private int _busyCount;

    protected ViewModelBase(ILogger logger)
    {
        Logger = logger;
    }

    protected ILogger Logger { get; }

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Problem with the last action, shown in a banner.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Confirmation of the last successful action.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Called when the page is shown.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    protected void ShowStatus(string message)
    {
        ErrorMessage = null;
        StatusMessage = message;
    }

    protected void ShowError(string message)
    {
        StatusMessage = null;
        ErrorMessage = message;
    }

    /// <summary>
    /// Runs data work off the UI thread (SQLite I/O is synchronous under the
    /// hood) and turns failures into a readable message. Returns false when
    /// the operation failed or was superseded.
    /// </summary>
    protected async Task<bool> RunAsync(
        Func<CancellationToken, Task> work,
        CancellationToken ct = default,
        bool clearMessages = true)
    {
        if (clearMessages)
        {
            ErrorMessage = null;
        }

        _busyCount++;
        IsBusy = true;

        try
        {
            await Task.Run(() => work(ct), ct);
            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (AppException ex)
        {
            ShowError(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error in {ViewModel}", GetType().Name);
            ShowError(
                "Something went wrong and the action was not completed. " +
                "No partial changes were saved. Details have been written to the log.");
            return false;
        }
        finally
        {
            _busyCount--;
            IsBusy = _busyCount > 0;
        }
    }

    /// <summary>Like <see cref="RunAsync"/> but returns a value (default on failure).</summary>
    protected async Task<(bool Ok, T? Value)> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default,
        bool clearMessages = true)
    {
        T? result = default;
        bool ok = await RunAsync(
            async token => { result = await work(token); },
            ct,
            clearMessages);
        return (ok, ok ? result : default);
    }
}
