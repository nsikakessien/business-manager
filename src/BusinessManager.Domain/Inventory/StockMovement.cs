namespace BusinessManager.Domain.Inventory;

/// <summary>
/// An immutable entry in a product's stock history.
/// </summary>
public sealed class StockMovement
{
    public const int MaxReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int QuantityChange { get; private set; }

    /// <summary>Quantity on hand immediately after this movement.</summary>
    public int QuantityAfter { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>The sale (or other document) that caused this movement, if any.</summary>
    public Guid? ReferenceId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private StockMovement()
    {
    }

    internal StockMovement(
        Guid productId,
        StockMovementType type,
        int quantityChange,
        int quantityAfter,
        string reason,
        Guid? referenceId)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Type = type;
        QuantityChange = quantityChange;
        QuantityAfter = quantityAfter;
        Reason = reason;
        ReferenceId = referenceId;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }
}
