using System.Collections.ObjectModel;
using BusinessManager.Application.Products;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.Support;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed partial class ProductsViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 50;
    private const int HistorySize = 100;

    private readonly IProductService _products;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
    private readonly LatestOnly _listLoads = new();
    private readonly LatestOnly _detailLoads = new();

    public ProductsViewModel(
        IProductService products,
        IDialogService dialogs,
        ILogger<ProductsViewModel> logger)
        : base(logger)
    {
        _products = products;
        _dialogs = dialogs;
    }

    public ObservableCollection<ProductListItem> Products { get; } = new();
    public ObservableCollection<StockMovementItem> Movements { get; } = new();

    // ----- List -----

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _includeArchived;

    [ObservableProperty]
    private bool _lowStockOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProductListItem? _selectedProduct;

    public bool HasSelection => SelectedProduct is not null;

    // ----- Editor -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool _isNew = true;

    public string EditorTitle => IsNew ? "New product" : "Edit product";

    [ObservableProperty]
    private string? _editSku;

    [ObservableProperty]
    private string? _editName;

    [ObservableProperty]
    private string? _editPrice;

    [ObservableProperty]
    private string? _editReorderLevel;

    [ObservableProperty]
    private string? _editOpeningStock;

    // ----- Stock -----

    [ObservableProperty]
    private bool _isReceiving = true;

    [ObservableProperty]
    private string? _stockQuantity;

    [ObservableProperty]
    private string? _stockReason;

    public override async Task ActivateAsync()
    {
        await LoadAsync(keepSelection: true);
    }

    partial void OnSearchTextChanged(string? value) =>
        _searchDebouncer.Run(_ => LoadAsync(resetPage: true));

    partial void OnIncludeArchivedChanged(bool value) => _ = LoadAsync(resetPage: true);

    partial void OnLowStockOnlyChanged(bool value) => _ = LoadAsync(resetPage: true);

    partial void OnSelectedProductChanged(ProductListItem? value)
    {
        if (value is null)
        {
            return;
        }

        IsNew = false;
        EditSku = value.Sku;
        EditName = value.Name;
        EditPrice = InputParser.FormatMoney(value.SellingPrice);
        EditReorderLevel = value.ReorderLevel.ToString();
        EditOpeningStock = null;
        StockQuantity = null;
        StockReason = null;
        _ = LoadMovementsAsync(value.Id);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(keepSelection: true);

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

    [RelayCommand]
    private void NewProduct()
    {
        SelectedProduct = null;
        Movements.Clear();
        IsNew = true;
        EditSku = EditName = EditPrice = EditReorderLevel = EditOpeningStock = null;
        ErrorMessage = StatusMessage = null;
    }

    [RelayCommand]
    private async Task SaveProductAsync()
    {
        if (!InputParser.TryParseMoney(EditPrice, "Selling price", out decimal price, out string? error) ||
            !InputParser.TryParseInt(EditReorderLevel, "Reorder level", out int reorder, out error, blankIsZero: true))
        {
            ShowError(error!);
            return;
        }

        if (IsNew)
        {
            if (!InputParser.TryParseInt(EditOpeningStock, "Opening stock", out int opening, out error, blankIsZero: true))
            {
                ShowError(error!);
                return;
            }

            var request = new CreateProductRequest(EditSku ?? "", EditName ?? "", price, reorder, opening);
            var (ok, id) = await RunAsync(ct => _products.CreateAsync(request, ct));
            if (ok)
            {
                ShowStatus($"Product {request.Sku.Trim().ToUpperInvariant()} created.");
                await LoadAsync(selectId: id);
            }
        }
        else if (SelectedProduct is { } selected)
        {
            var request = new UpdateProductRequest(selected.Id, EditName ?? "", price, reorder);
            if (await RunAsync(ct => _products.UpdateAsync(request, ct)))
            {
                ShowStatus("Product saved.");
                await LoadAsync(selectId: selected.Id);
            }
        }
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync()
    {
        if (SelectedProduct is not { } selected)
        {
            return;
        }

        bool archive = selected.IsActive;

        if (archive && !_dialogs.Confirm(
                $"Archive {selected.Name}? It will no longer be available for sale, " +
                "but its history is kept and it can be restored later."))
        {
            return;
        }

        if (await RunAsync(ct => _products.SetActiveAsync(selected.Id, !archive, ct)))
        {
            ShowStatus(archive ? $"{selected.Name} archived." : $"{selected.Name} restored.");
            await LoadAsync(selectId: selected.Id);
        }
    }

    [RelayCommand]
    private async Task ApplyStockAsync()
    {
        if (SelectedProduct is not { } selected)
        {
            return;
        }

        if (!InputParser.TryParseInt(StockQuantity, "Quantity", out int quantity, out string? error, allowNegative: !IsReceiving))
        {
            ShowError(error!);
            return;
        }

        string reason = string.IsNullOrWhiteSpace(StockReason)
            ? (IsReceiving ? "Stock received" : "")
            : StockReason;

        bool receiving = IsReceiving;
        bool ok = await RunAsync(ct => receiving
            ? _products.ReceiveStockAsync(selected.Id, quantity, reason, ct)
            : _products.AdjustStockAsync(selected.Id, quantity, reason, ct));

        if (ok)
        {
            ShowStatus(receiving
                ? $"Received {quantity:N0} × {selected.Name}."
                : $"Adjusted {selected.Name} by {quantity:+#,0;-#,0}.");
            StockQuantity = null;
            StockReason = null;
            await LoadAsync(selectId: selected.Id);
        }
    }

    private async Task LoadAsync(bool resetPage = false, bool keepSelection = false, Guid? selectId = null)
    {
        if (resetPage)
        {
            Page = 1;
        }

        Guid? wanted = selectId ?? (keepSelection ? SelectedProduct?.Id : null);
        var query = new ProductQuery(SearchText, IncludeArchived, LowStockOnly, Page, PageSize);
        var token = _listLoads.Next();

        var (ok, result) = await RunAsync(ct => _products.SearchAsync(query, ct), token, clearMessages: false);
        if (!ok || result is null)
        {
            return;
        }

        // Deleted-elsewhere or out-of-range page: fall back to the last page.
        if (result.Items.Count == 0 && result.TotalCount > 0 && Page > 1)
        {
            Page = result.PageCount;
            await LoadAsync(selectId: wanted);
            return;
        }

        Products.Clear();
        foreach (var item in result.Items)
        {
            Products.Add(item);
        }

        TotalCount = result.TotalCount;
        PageCount = result.PageCount;

        if (wanted is { } id)
        {
            var match = Products.FirstOrDefault(p => p.Id == id);
            if (match is null)
            {
                // Selected product isn't in this page (e.g. filtered out); load it directly.
                var (found, product) = await RunAsync(ct => _products.GetAsync(id, ct), clearMessages: false);
                match = found ? product : null;
            }

            if (match is null)
            {
                NewProduct();
            }
            else if (Equals(SelectedProduct, match))
            {
                // Same values: the property won't raise a change, so refresh the editor explicitly.
                OnSelectedProductChanged(match);
            }
            else
            {
                SelectedProduct = match;
            }
        }
    }

    private async Task LoadMovementsAsync(Guid productId)
    {
        var token = _detailLoads.Next();
        var (ok, history) = await RunAsync(
            ct => _products.GetMovementsAsync(productId, 1, HistorySize, ct),
            token,
            clearMessages: false);

        if (!ok || history is null)
        {
            return;
        }

        Movements.Clear();
        foreach (var movement in history.Items)
        {
            Movements.Add(movement);
        }
    }

    public void Dispose() => _searchDebouncer.Dispose();
}
