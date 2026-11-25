using System.Collections.ObjectModel;
using BusinessManager.Application.Sales;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.Support;
using BusinessManager.Domain.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed partial class SalesHistoryViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 50;

    private readonly ISalesService _sales;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
    private readonly LatestOnly _listLoads = new();
    private readonly LatestOnly _detailLoads = new();

    public SalesHistoryViewModel(
        ISalesService sales,
        IDialogService dialogs,
        ILogger<SalesHistoryViewModel> logger)
        : base(logger)
    {
        _sales = sales;
        _dialogs = dialogs;
    }

    public ObservableCollection<SaleListItem> Sales { get; } = new();

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private bool _includeVoided = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private SaleListItem? _selectedSale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVoid))]
    [NotifyCanExecuteChangedFor(nameof(VoidSaleCommand))]
    private SaleDetails? _details;

    public bool CanVoid => Details?.Status == SaleStatus.Completed;

    public override Task ActivateAsync() => LoadAsync();

    partial void OnSearchTextChanged(string? value) => _searchDebouncer.Run(_ => LoadAsync(resetPage: true));

    partial void OnFromDateChanged(DateTime? value) => _ = LoadAsync(resetPage: true);

    partial void OnToDateChanged(DateTime? value) => _ = LoadAsync(resetPage: true);

    partial void OnIncludeVoidedChanged(bool value) => _ = LoadAsync(resetPage: true);

    partial void OnSelectedSaleChanged(SaleListItem? value)
    {
        if (value is not null)
        {
            _ = LoadDetailsAsync(value.Id);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void Today()
    {
        FromDate = ToDate = DateTime.Today;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = null;
        FromDate = ToDate = null;
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync()
    {
        Page--;
        await LoadAsync();
    }

    private bool CanGoPrevious() => Page > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        Page++;
        await LoadAsync();
    }

    private bool CanGoNext() => Page < PageCount;

    [RelayCommand(CanExecute = nameof(CanVoid))]
    private async Task VoidSaleAsync()
    {
        if (Details is not { Status: SaleStatus.Completed } sale)
        {
            return;
        }

        string impact = sale.AmountOnAccount > 0
            ? $" {sale.AmountOnAccount:N2} will be removed from {sale.CustomerName}'s account."
            : string.Empty;
        string refund = sale.AmountPaid > 0
            ? $" Refund {sale.AmountPaid:N2} paid by {EnumDisplayConverter.Humanize(sale.PaymentMethod?.ToString() ?? "")} separately."
            : string.Empty;

        string? reason = _dialogs.Prompt(
            $"Void sale {sale.Number}? All items will be returned to stock.{impact}{refund}\n\nReason:",
            "Void sale",
            Sale.MaxVoidReasonLength);

        if (reason is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            ShowError("A reason is required to void a sale.");
            return;
        }

        if (await RunAsync(ct => _sales.VoidAsync(sale.Id, reason, ct)))
        {
            ShowStatus($"Sale {sale.Number} voided.");
            await LoadAsync();
            await LoadDetailsAsync(sale.Id);
        }
    }

    private async Task LoadAsync(bool resetPage = false)
    {
        if (resetPage)
        {
            Page = 1;
        }

        var query = new SaleQuery(
            SearchText,
            FromDate is { } f ? DateOnly.FromDateTime(f) : null,
            ToDate is { } t ? DateOnly.FromDateTime(t) : null,
            IncludeVoided,
            Page,
            PageSize);

        var token = _listLoads.Next();
        var (ok, result) = await RunAsync(ct => _sales.SearchAsync(query, ct), token, clearMessages: false);
        if (!ok || result is null)
        {
            return;
        }

        if (result.Items.Count == 0 && result.TotalCount > 0 && Page > 1)
        {
            Page = result.PageCount;
            await LoadAsync();
            return;
        }

        Guid? selected = SelectedSale?.Id ?? Details?.Id;

        Sales.Clear();
        foreach (var sale in result.Items)
        {
            Sales.Add(sale);
        }

        TotalCount = result.TotalCount;
        PageCount = result.PageCount;
        SelectedSale = Sales.FirstOrDefault(s => s.Id == selected);
    }

    private async Task LoadDetailsAsync(Guid saleId)
    {
        var token = _detailLoads.Next();
        var (ok, details) = await RunAsync(ct => _sales.GetAsync(saleId, ct), token, clearMessages: false);
        if (ok)
        {
            Details = details;
        }
    }

    public void Dispose() => _searchDebouncer.Dispose();
}
