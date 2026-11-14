namespace BusinessManager.Domain.Inventory;

public sealed class StockMovement
{
    public Guid Id { get; }
    public Guid ProductId { get; }
    public StockMovementType Type { get; }
    public int QuantityChange { get; }
    public string Reason { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    internal StockMovement(
        Guid productId,
        StockMovementType type,
        int quantityChange,
        string reason)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Type = type;
        QuantityChange = quantityChange;
        Reason = reason;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }
}