using System.Collections.ObjectModel;
using System.Globalization;
using BusinessManager.Application.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed record DayBar(string Label, decimal Revenue, int SaleCount, double BarHeight);

public sealed partial class DashboardViewModel : ViewModelBase
{
    private const double MaxBarHeight = 140;

    private readonly IDashboardService _dashboard;

    public DashboardViewModel(IDashboardService dashboard, ILogger<DashboardViewModel> logger)
        : base(logger)
    {
        _dashboard = dashboard;
    }

    [ObservableProperty]
    private DashboardSummary? _summary;

    public ObservableCollection<DayBar> Days { get; } = new();

    public override Task ActivateAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var (ok, summary) = await RunAsync(ct => _dashboard.GetSummaryAsync(ct));
        if (!ok || summary is null)
        {
            return;
        }

        Summary = summary;

        decimal max = summary.Last7Days.Max(d => d.Revenue);
        Days.Clear();
        foreach (var day in summary.Last7Days)
        {
            double height = max == 0 ? 2 : Math.Max(2, (double)(day.Revenue / max) * MaxBarHeight);
            Days.Add(new DayBar(
                day.Date.ToString("ddd d", CultureInfo.CurrentCulture),
                day.Revenue,
                day.SaleCount,
                height));
        }
    }
}
