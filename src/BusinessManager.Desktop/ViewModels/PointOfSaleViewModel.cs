using System.Collections.ObjectModel;
using BusinessManager.Application.Common;
using BusinessManager.Application.Customers;
using BusinessManager.Application.Products;
using BusinessManager.Application.Sales;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.Support;
using BusinessManager.Domain.Common;
using BusinessManager.Domain.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed partial class PointOfSaleViewModel : ViewModelBase, IDisposable
{
    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly ISalesService _sales;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _productSearch = new(TimeSpan.FromMilliseconds(250));
    private readonly Debouncer _customerSearch = new(TimeSpan.FromMilliseconds(300));

    public PointOfSaleViewModel(
        IProductService products,
        ICustomerService customers,
        ISalesService sales,
        IDialogService dialogs,
        ILogger<PointOfSaleViewModel> logger)
        : base(logger)
    {
        _products = products;
        _customers = customers;
        _sales = sales;
        _dialogs = dialogs;
        Cart.CollectionChanged += (_, _) => Recalculate();
    }

    public ObservableCollection<ProductListItem> ProductResults { get; } = new();
    public ObservableCollection<CustomerListItem> CustomerResults { get; } = new();
    public ObservableCollection<CartLineViewModel> Cart { get; } = new();
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = Enum.GetValues<PaymentMethod>();

    public bool HasUnsavedWork => Cart.Count > 0;

    [ObservableProperty]
    private string? _productSearchText;

    [ObservableProperty]
    private ProductListItem? _selectedProductResult;

    [ObservableProperty]
    private string? _customerSearchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    private CustomerListItem? _selectedCustomer;

    public bool HasCustomer => SelectedCustomer is not null;

    [ObservableProperty]
    private string? _discountText;

    /// <summary>Blank means the exact total was paid.</summary>
    [ObservableProperty]
    private string? _amountReceivedText;

    [ObservableProperty]
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private decimal _subtotal;

    [ObservableProperty]
    private decimal _discount;

    [ObservableProperty]
    private decimal _total;

    [ObservableProperty]
    private decimal _amountPaid;

    [ObservableProperty]
    private decimal _change;

    [ObservableProperty]
    private decimal _onAccount;

    /// <summary>Why the sale can't be completed yet, if anything.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSaleCommand))]
    private string? _validationMessage = "Add products to start a sale.";

    [ObservableProperty]
    private SaleDetails? _lastSale;

    partial void OnProductSearchTextChanged(string? value) =>
        _productSearch.Run(SearchProductsAsync);

    partial void OnCustomerSearchTextChanged(string? value) =>
        _customerSearch.Run(SearchCustomersAsync);

    partial void OnSelectedCustomerChanged(CustomerListItem? value) => Recalculate();

    partial void OnDiscountTextChanged(string? value) => Recalculate();

    partial void OnAmountReceivedTextChanged(string? value) => Recalculate();

    partial void OnPaymentMethodChanged(PaymentMethod value) => Recalculate();

    private async Task SearchProductsAsync(CancellationToken ct)
    {
        string? text = ProductSearchText;
        if (string.IsNullOrWhiteSpace(text))
        {
            ProductResults.Clear();
            return;
        }

        var (ok, results) = await RunAsync(token => _products.FindForSaleAsync(text, 20, token), ct, clearMessages: false);
        if (!ok || results is null)
        {
            return;
        }

        ProductResults.Clear();
        foreach (var product in results)
        {
            ProductResults.Add(product);
        }

        SelectedProductResult = ProductResults.FirstOrDefault();
    }

    private async Task SearchCustomersAsync(CancellationToken ct)
    {
        string? text = CustomerSearchText;
        var query = new CustomerQuery(text, PageSize: 20);

        var (ok, results) = await RunAsync(token => _customers.SearchAsync(query, token), ct, clearMessages: false);
        if (!ok || results is null)
        {
            return;
        }

        // Clearing the list would make the ComboBox drop the current
        // selection, so remember it and restore it with fresh figures.
        Guid? selectedId = SelectedCustomer?.Id;

        CustomerResults.Clear();
        foreach (var customer in results.Items)
        {
            CustomerResults.Add(customer);
        }

        if (selectedId is { } id)
        {
            var match = CustomerResults.FirstOrDefault(c => c.Id == id);
            if (match is null)
            {
                var (found, fresh) = await RunAsync(token => _customers.GetAsync(id, token), ct, clearMessages: false);
                if (found && fresh is { IsActive: true })
                {
                    CustomerResults.Insert(0, fresh);
                    match = fresh;
                }
            }

            SelectedCustomer = match;
        }
    }

    /// <summary>
    /// Enter in the search box: a barcode scanner types the SKU and presses
    /// Enter, so an exact SKU match is added immediately.
    /// </summary>
    [RelayCommand]
    private async Task AddFromSearchAsync()
    {
        string? text = ProductSearchText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Search directly instead of waiting for the debounce, so a fast
        // scanner never adds a stale result.
        var (ok, results) = await RunAsync(ct => _products.FindForSaleAsync(text, 20, ct), clearMessages: false);
        if (!ok || results is null)
        {
            return;
        }

        var exact = results.FirstOrDefault(p => string.Equals(p.Sku, text, StringComparison.OrdinalIgnoreCase));
        var pick = exact ?? (results.Count == 1 ? results[0] : null);

        if (pick is null)
        {
            ProductResults.Clear();
            foreach (var product in results)
            {
                ProductResults.Add(product);
            }

            ShowError(results.Count == 0 ? $"No product matches \"{text}\"." : "Several products match. Pick one from the list.");
            return;
        }

        AddProduct(pick);
        ProductSearchText = null;
        ProductResults.Clear();
    }

    [RelayCommand]
    private void AddProduct(ProductListItem? product)
    {
        product ??= SelectedProductResult;
        if (product is null)
        {
            return;
        }

        ErrorMessage = null;
        LastSale = null;

        var existing = Cart.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.SetQuantity((existing.Quantity ?? 0) + 1);
            existing.Available = product.QuantityOnHand;
            return;
        }

        if (Cart.Count >= Sale.MaxLines)
        {
            ShowError($"A sale cannot have more than {Sale.MaxLines} lines.");
            return;
        }

        if (product.QuantityOnHand <= 0)
        {
            ShowError($"{product.Name} is out of stock.");
            return;
        }

        Cart.Add(new CartLineViewModel(product, 1, Recalculate));
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is not null)
        {
            Cart.Remove(line);
        }
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        SelectedCustomer = null;
        CustomerSearchText = null;
    }

    [RelayCommand]
    private void NewSale()
    {
        if (Cart.Count > 0 && !_dialogs.Confirm("Discard the current sale?"))
        {
            return;
        }

        Reset();
        LastSale = null;
        ErrorMessage = StatusMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanCompleteSale))]
    private async Task CompleteSaleAsync()
    {
        Recalculate();
        if (ValidationMessage is not null)
        {
            ShowError(ValidationMessage);
            return;
        }

        var request = new CreateSaleRequest(
            Cart.Select(l => new SaleLineRequest(l.ProductId, l.Quantity!.Value)).ToList(),
            SelectedCustomer?.Id,
            Discount,
            AmountPaid,
            AmountPaid > 0 ? PaymentMethod : null,
            Notes,
            ExpectedTotal: Total);

        if (OnAccount > 0 && SelectedCustomer is { } customer &&
            !_dialogs.Confirm($"{OnAccount:N2} will be added to {customer.Name}'s account. Continue?"))
        {
            return;
        }

        decimal change = Change;
        bool pricesChanged = false;

        var (ok, sale) = await RunAsync(async ct =>
        {
            try
            {
                return await _sales.CreateAsync(request, ct);
            }
            catch (PriceChangedException)
            {
                pricesChanged = true;
                throw;
            }
        });

        if (pricesChanged)
        {
            await RefreshCartPricesAsync();
            return;
        }

        if (!ok || sale is null)
        {
            // Stock may have changed (e.g. sold at another till); refresh what we show.
            await RefreshCartPricesAsync(keepError: true);
            return;
        }

        Reset();
        LastSale = sale;
        ShowStatus(change > 0
            ? $"Sale {sale.Number} completed. Give change: {change:N2}"
            : $"Sale {sale.Number} completed.");

        // Balances changed if the sale went on account.
        await SearchCustomersAsync(CancellationToken.None);
    }

    // AsyncRelayCommand also disables itself while running, so a double
    // click can never submit the same sale twice.
    private bool CanCompleteSale() => ValidationMessage is null;

    private async Task RefreshCartPricesAsync(bool keepError = false)
    {
        var ids = Cart.Select(l => l.ProductId).ToList();
        string? error = ErrorMessage;

        var (ok, current) = await RunAsync(async ct =>
        {
            var list = new List<ProductListItem>();
            foreach (var id in ids)
            {
                try
                {
                    list.Add(await _products.GetAsync(id, ct));
                }
                catch (NotFoundException)
                {
                }
            }

            return list;
        }, clearMessages: false);

        if (!ok || current is null)
        {
            return;
        }

        foreach (var line in Cart.ToList())
        {
            var product = current.FirstOrDefault(p => p.Id == line.ProductId);
            if (product is null || !product.IsActive)
            {
                Cart.Remove(line);
                continue;
            }

            line.UnitPrice = product.SellingPrice;
            line.Available = product.QuantityOnHand;
        }

        Recalculate();

        if (keepError)
        {
            ErrorMessage = error;
        }
        else
        {
            ShowError(error + " Prices in the cart have been updated.");
        }
    }

    private void Reset()
    {
        Cart.Clear();
        SelectedCustomer = null;
        CustomerSearchText = null;
        DiscountText = null;
        AmountReceivedText = null;
        PaymentMethod = PaymentMethod.Cash;
        Notes = null;
        ProductSearchText = null;
        ProductResults.Clear();
        Recalculate();
    }

    private void Recalculate()
    {
        string? problem = null;

        decimal subtotal = 0m;
        foreach (var line in Cart)
        {
            if (line.Quantity is null)
            {
                problem ??= $"Check the quantity for {line.Name}.";
            }
            else if (line.Quantity > line.Available)
            {
                problem ??= $"Only {line.Available:N0} × {line.Name} in stock.";
            }

            subtotal += line.LineTotal;
        }

        if (Cart.Count == 0)
        {
            problem = "Add products to start a sale.";
        }

        if (!InputParser.TryParseMoney(DiscountText, "Discount", out decimal discount, out string? error, blankIsZero: true))
        {
            problem ??= error;
            discount = 0m;
        }
        else if (discount > subtotal)
        {
            problem ??= "Discount cannot be more than the subtotal.";
            discount = 0m;
        }

        decimal total = subtotal - discount;
        decimal received = total;
        if (!string.IsNullOrWhiteSpace(AmountReceivedText))
        {
            if (!InputParser.TryParseMoney(AmountReceivedText, "Amount received", out received, out error))
            {
                problem ??= error;
                received = 0m;
            }
        }

        decimal paid = Math.Min(received, total);
        decimal change = received > total ? received - total : 0m;
        decimal onAccount = total - paid;

        if (change > 0 && PaymentMethod != PaymentMethod.Cash)
        {
            problem ??= "Only cash payments can be more than the total.";
        }

        if (onAccount > 0)
        {
            if (SelectedCustomer is null)
            {
                problem ??= $"{onAccount:N2} is unpaid. Select a customer to put it on account, or take full payment.";
            }
            else if (onAccount > SelectedCustomer.AvailableCredit)
            {
                problem ??= $"{SelectedCustomer.Name} has only {SelectedCustomer.AvailableCredit:N2} credit available.";
            }
        }

        Subtotal = subtotal;
        Discount = discount;
        Total = total;
        AmountPaid = paid;
        Change = change;
        OnAccount = onAccount;
        ValidationMessage = problem;
    }

    public override async Task ActivateAsync()
    {
        // Customers or balances may have changed on another page.
        await SearchCustomersAsync(CancellationToken.None);

        if (Cart.Count > 0)
        {
            await RefreshCartPricesAsync(keepError: true);
        }
    }

    public void Dispose()
    {
        _productSearch.Dispose();
        _customerSearch.Dispose();
    }
}
