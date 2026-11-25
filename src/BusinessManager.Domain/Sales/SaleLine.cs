namespace BusinessManager.Domain.Sales;

/// <summary>
/// One product on a sale. Product details are copied so the sale record stays
/// accurate after the product is renamed or repriced.
/// </summary>
public sealed class SaleLine
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    private SaleLine()
    {
    }

    internal SaleLine(
        Guid saleId,
        Guid productId,
        string sku,
        string productName,
        decimal unitPrice,
        int quantity)
    {
        Id = Guid.NewGuid();
        SaleId = saleId;
        ProductId = productId;
        Sku = sku;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = unitPrice * quantity;
    }
}
