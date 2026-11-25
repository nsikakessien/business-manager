using System.Diagnostics;
using BusinessManager.Desktop.Services;
using BusinessManager.Infrastructure.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed record NavigationItem(string Title, string Glyph, ViewModelBase Page);

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly DatabaseBackupService _backups;
    private readonly IDialogService _dialogs;
    private readonly PointOfSaleViewModel _pointOfSale;

    public MainViewModel(
        DashboardViewModel dashboard,
        PointOfSaleViewModel pointOfSale,
        SalesHistoryViewModel salesHistory,
        ProductsViewModel products,
        CustomersViewModel customers,
        DatabaseBackupService backups,
        IDialogService dialogs,
        ILogger<MainViewModel> logger)
        : base(logger)
    {
        _backups = backups;
        _dialogs = dialogs;
        _pointOfSale = pointOfSale;

        // Glyphs are from the Segoe MDL2 Assets font that ships with Windows.
        Navigation =
        [
            new("Dashboard", "", dashboard),
            new("New sale", "", pointOfSale),
            new("Sales", "", salesHistory),
            new("Products", "", products),
            new("Customers", "", customers)
        ];
    }

    public IReadOnlyList<NavigationItem> Navigation { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public bool HasUnsavedWork => _pointOfSale.HasUnsavedWork;

    partial void OnSelectedNavigationChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        _ = value.Page.ActivateAsync();
    }

    public void Start() => SelectedNavigation = Navigation[0];

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        var (ok, path) = await RunAsync(ct => _backups.CreateBackupAsync("manual", ct));
        if (ok)
        {
            _dialogs.ShowInfo($"Backup saved to:\n{path}");
        }
        else if (ErrorMessage is { } error)
        {
            _dialogs.ShowError(error);
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(_backups.BackupDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _backups.BackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Logger.LogWarning(ex, "Could not open backup folder");
            _dialogs.ShowError($"Could not open the backup folder:\n{_backups.BackupDirectory}");
        }
    }
}
