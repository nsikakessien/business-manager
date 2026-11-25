using BusinessManager.Domain.Common;

namespace BusinessManager.Domain.Inventory;

/// <summary>
/// Quantity on hand for one product.
/// </summary>
/// <remarks>
/// <see cref="Movements"/> holds the movements recorded through this instance.
/// When the stock is loaded from storage the full history is not loaded (it
/// can grow without bound); query the movement history separately instead.
/// </remarks>
public sealed class InventoryStock
{
    private readonly List<StockMovement> _movements = new();

    public Guid ProductId { get; private set; }
    public int QuantityOnHand { get; private set; }

    public IReadOnlyList<StockMovement> Movements => _movements.AsReadOnly();

    private InventoryStock()
    {
    }

    public InventoryStock(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException(
                "A product ID is required.",
                nameof(productId));
        }

        ProductId = productId;
    }

    public StockMovement Receive(int quantity, string reason)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Received quantity must be greater than zero.");
        }

        return RecordMovement(
            StockMovementType.Receipt,
            quantity,
            reason,
            referenceId: null);
    }

    public StockMovement Adjust(int quantityChange, string reason)
    {
        if (quantityChange == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityChange),
                "A stock adjustment cannot be zero.");
        }

        return RecordMovement(
            StockMovementType.Adjustment,
            quantityChange,
            reason,
            referenceId: null);
    }

    public StockMovement RemoveForSale(int quantity, Guid saleId, string reason)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Sold quantity must be greater than zero.");
        }

        if (quantity > QuantityOnHand)
        {
            throw new DomainException(
                $"Insufficient stock: {QuantityOnHand:N0} on hand, " +
                $"{quantity:N0} requested.");
        }

        return RecordMovement(
            StockMovementType.Sale,
            -quantity,
            reason,
            RequireReference(saleId));
    }

    public StockMovement ReturnFromVoidedSale(int quantity, Guid saleId, string reason)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Returned quantity must be greater than zero.");
        }

        return RecordMovement(
            StockMovementType.SaleVoid,
            quantity,
            reason,
            RequireReference(saleId));
    }

    private static Guid RequireReference(Guid saleId)
    {
        if (saleId == Guid.Empty)
        {
            throw new ArgumentException("A sale ID is required.", nameof(saleId));
        }

        return saleId;
    }

    private StockMovement RecordMovement(
        StockMovementType type,
        int quantityChange,
        string reason,
        Guid? referenceId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A reason is required for every stock movement.",
                nameof(reason));
        }

        string trimmedReason = Guard.RequiredText(
            reason,
            StockMovement.MaxReasonLength,
            nameof(reason),
            "Reason");

        int newQuantity = checked(QuantityOnHand + quantityChange);

        if (newQuantity < 0)
        {
            throw new DomainException(
                "This movement would make stock negative " +
                $"({QuantityOnHand:N0} on hand).");
        }

        var movement = new StockMovement(
            ProductId,
            type,
            quantityChange,
            newQuantity,
            trimmedReason,
            referenceId);

        _movements.Add(movement);
        QuantityOnHand = newQuantity;

        return movement;
    }
}
