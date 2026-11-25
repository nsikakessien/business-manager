using BusinessManager.Application.Products;
using BusinessManager.Desktop.Support;
using BusinessManager.Domain.Sales;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessManager.Desktop.ViewModels;

public sealed partial class CartLineViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public CartLineViewModel(ProductListItem product, int quantity, Action onChanged)
    {
        ProductId = product.Id;
        Sku = product.Sku;
        Name = product.Name;
        _unitPrice = product.SellingPrice;
        _available = product.QuantityOnHand;
        _onChanged = onChanged;
        _quantityText = quantity.ToString();
        Quantity = quantity;
    }

    public Guid ProductId { get; }
    public string Sku { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _unitPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning))]
    private int _available;

    [ObservableProperty]
    private string _quantityText;

    /// <summary>Parsed quantity, or null when the text is not a valid quantity.</summary>
    public int? Quantity { get; private set; }

    public decimal LineTotal => Quantity is { } q ? UnitPrice * q : 0m;

    public string? Warning =>
        Quantity is null ? "Enter a whole number of at least 1"
        : Quantity > Available ? $"Only {Available:N0} in stock"
        : null;

    public void SetQuantity(int quantity) => QuantityText = quantity.ToString();

    partial void OnQuantityTextChanged(string value)
    {
        Quantity = InputParser.TryParseInt(value, "Quantity", out int q, out _) && q is > 0 and <= Sale.MaxLineQuantity
            ? q
            : null;

        OnPropertyChanged(nameof(Quantity));
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(Warning));
        _onChanged();
    }

    partial void OnUnitPriceChanged(decimal value) => _onChanged();
}
