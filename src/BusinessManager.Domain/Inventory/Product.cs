using System.Text.RegularExpressions;
using BusinessManager.Domain.Common;

namespace BusinessManager.Domain.Inventory;

public sealed partial class Product
{
    public const int MaxSkuLength = 50;
    public const int MaxNameLength = 200;
    public const int MaxReorderLevel = 1_000_000_000;

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal SellingPrice { get; private set; }

    /// <summary>
    /// Stock at or below this level is reported as low. Zero disables the alert.
    /// </summary>
    public int ReorderLevel { get; private set; }

    /// <summary>
    /// Archived products keep their history but cannot be sold.
    /// </summary>
    public bool IsActive { get; private set; }

    private Product()
    {
    }

    public Product(
        string sku,
        string name,
        decimal sellingPrice,
        int reorderLevel = 0)
    {
        string normalizedSku = NormalizeSku(sku);
        string normalizedName = ValidateName(name);
        decimal price = ValidatePrice(sellingPrice);
        int level = ValidateReorderLevel(reorderLevel);

        Id = Guid.NewGuid();
        Sku = normalizedSku;
        Name = normalizedName;
        SellingPrice = price;
        ReorderLevel = level;
        IsActive = true;
    }

    public void UpdateDetails(
        string name,
        decimal sellingPrice,
        int reorderLevel)
    {
        // Validate everything before changing anything so a failed update
        // never leaves the product half modified.
        string normalizedName = ValidateName(name);
        decimal price = ValidatePrice(sellingPrice);
        int level = ValidateReorderLevel(reorderLevel);

        Name = normalizedName;
        SellingPrice = price;
        ReorderLevel = level;
    }

    public void Archive() => IsActive = false;

    public void Restore() => IsActive = true;

    public static string NormalizeSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException(
                "A product SKU is required.",
                nameof(sku));
        }

        string normalized = sku.Trim().ToUpperInvariant();

        if (normalized.Length > MaxSkuLength)
        {
            throw new ArgumentException(
                $"SKU cannot be longer than {MaxSkuLength} characters.",
                nameof(sku));
        }

        if (!SkuPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "SKU may only contain letters, digits, '-', '_', '.' and '/', " +
                "and must start with a letter or digit.",
                nameof(sku));
        }

        return normalized;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A product name is required.",
                nameof(name));
        }

        return Guard.RequiredText(name, MaxNameLength, nameof(name), "Product name");
    }

    private static decimal ValidatePrice(decimal sellingPrice) =>
        Guard.Amount(sellingPrice, nameof(sellingPrice), "Selling price");

    private static int ValidateReorderLevel(int reorderLevel)
    {
        if (reorderLevel is < 0 or > MaxReorderLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reorderLevel),
                $"Reorder level must be between 0 and {MaxReorderLevel:N0}.");
        }

        return reorderLevel;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
