namespace BusinessManager.Domain.Inventory;

public sealed class Product
{
    public Guid Id { get; }
    public string Sku { get; }
    public string Name { get; }
    public decimal SellingPrice { get; }

    public Product(string sku, string name, decimal sellingPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException(
                "A product SKU is required.",
                nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A product name is required.",
                nameof(name));
        }

        if (sellingPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sellingPrice),
                "Selling price cannot be negative.");
        }

        if (decimal.Round(sellingPrice, 2) != sellingPrice)
        {
            throw new ArgumentException(
                "Selling price must have at most two decimal places.",
                nameof(sellingPrice));
        }

        Id = Guid.NewGuid();
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        SellingPrice = sellingPrice;
    }
}