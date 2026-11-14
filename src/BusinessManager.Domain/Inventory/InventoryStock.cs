using System.Collections.ObjectModel;

namespace BusinessManager.Domain.Inventory;

public sealed class InventoryStock
{
    private readonly List<StockMovement> _movements = new();

    public Guid ProductId { get; }
    public int QuantityOnHand { get; private set; }

    public ReadOnlyCollection<StockMovement> Movements { get; }

    public InventoryStock(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException(
                "A product ID is required.",
                nameof(productId));
        }

        ProductId = productId;
        Movements = _movements.AsReadOnly();
    }

    public void Receive(int quantity, string reason)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Received quantity must be greater than zero.");
        }

        RecordMovement(
            StockMovementType.Receipt,
            quantity,
            reason);
    }

    public void Adjust(int quantityChange, string reason)
    {
        if (quantityChange == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityChange),
                "A stock adjustment cannot be zero.");
        }

        RecordMovement(
            StockMovementType.Adjustment,
            quantityChange,
            reason);
    }

    private void RecordMovement(
        StockMovementType type,
        int quantityChange,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A reason is required for every stock movement.",
                nameof(reason));
        }

        int newQuantity = checked(QuantityOnHand + quantityChange);

        if (newQuantity < 0)
        {
            throw new InvalidOperationException(
                "This movement would make stock negative.");
        }

        var movement = new StockMovement(
            ProductId,
            type,
            quantityChange,
            reason.Trim());

        _movements.Add(movement);
        QuantityOnHand = newQuantity;
    }
}