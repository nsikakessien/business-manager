using BusinessManager.Domain.Inventory;
using BusinessManager.Domain.Common;
using Xunit;

namespace BusinessManager.Domain.Tests.Inventory;

public sealed class InventoryStockTests
{
    [Fact]
    public void Receive_IncreasesStockAndRecordsMovement()
    {
        var productId = Guid.NewGuid();
        var stock = new InventoryStock(productId);

        Assert.Equal(0, stock.QuantityOnHand);
        Assert.Empty(stock.Movements);

        stock.Receive(20, "  Opening stock  ");

        Assert.Equal(20, stock.QuantityOnHand);

        var movement = Assert.Single(stock.Movements);

        Assert.NotEqual(Guid.Empty, movement.Id);
        Assert.Equal(productId, movement.ProductId);
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Equal(20, movement.QuantityChange);
        Assert.Equal("Opening stock", movement.Reason);
        Assert.Equal(TimeSpan.Zero, movement.OccurredAtUtc.Offset);
    }

    [Fact]
    public void Adjust_RecordsPositiveAndNegativeCorrections()
    {
        var stock = new InventoryStock(Guid.NewGuid());

        stock.Receive(20, "Opening stock");
        stock.Adjust(-3, "Damaged cartons");
        stock.Adjust(2, "Previously uncounted cartons");

        Assert.Equal(19, stock.QuantityOnHand);
        Assert.Equal(3, stock.Movements.Count);

        var removal = stock.Movements[1];

        Assert.Equal(StockMovementType.Adjustment, removal.Type);
        Assert.Equal(-3, removal.QuantityChange);
        Assert.Equal("Damaged cartons", removal.Reason);

        var addition = stock.Movements[2];

        Assert.Equal(StockMovementType.Adjustment, addition.Type);
        Assert.Equal(2, addition.QuantityChange);

        Assert.Equal(
            stock.QuantityOnHand,
            stock.Movements.Sum(movement => movement.QuantityChange));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Receive_WithNonPositiveQuantity_LeavesStockUnchanged(
        int quantity)
    {
        var stock = new InventoryStock(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            stock.Receive(quantity, "Delivery"));

        Assert.Equal(0, stock.QuantityOnHand);
        Assert.Empty(stock.Movements);
    }

    [Fact]
    public void Adjust_WithZeroChange_LeavesStockUnchanged()
    {
        var stock = new InventoryStock(Guid.NewGuid());
        stock.Receive(10, "Opening stock");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            stock.Adjust(0, "Stock count"));

        Assert.Equal(10, stock.QuantityOnHand);
        Assert.Single(stock.Movements);
    }

    [Fact]
    public void Adjust_BelowZero_LeavesStockAndHistoryUnchanged()
    {
        var stock = new InventoryStock(Guid.NewGuid());
        stock.Receive(5, "Opening stock");

        Assert.Throws<DomainException>(() =>
            stock.Adjust(-6, "Damaged stock"));

        Assert.Equal(5, stock.QuantityOnHand);
        Assert.Single(stock.Movements);
    }

    [Fact]
    public void Adjust_ToExactlyZero_IsAllowed()
    {
        var stock = new InventoryStock(Guid.NewGuid());
        stock.Receive(5, "Opening stock");

        stock.Adjust(-5, "All remaining stock damaged");

        Assert.Equal(0, stock.QuantityOnHand);
        Assert.Equal(2, stock.Movements.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Operations_WithMissingReason_LeaveStockUnchanged(
        string? reason)
    {
        var stock = new InventoryStock(Guid.NewGuid());
        stock.Receive(10, "Opening stock");

        Assert.Throws<ArgumentException>(() =>
            stock.Receive(2, reason!));

        Assert.Throws<ArgumentException>(() =>
            stock.Adjust(-2, reason!));

        Assert.Equal(10, stock.QuantityOnHand);
        Assert.Single(stock.Movements);
    }

    [Fact]
    public void Constructor_WithEmptyProductId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new InventoryStock(Guid.Empty));
    }

    [Fact]
    public void Receive_WhenQuantityWouldOverflow_LeavesStockUnchanged()
    {
        var stock = new InventoryStock(Guid.NewGuid());
        stock.Receive(int.MaxValue, "Opening stock");

        Assert.Throws<OverflowException>(() =>
            stock.Receive(1, "Additional delivery"));

        Assert.Equal(int.MaxValue, stock.QuantityOnHand);
        Assert.Single(stock.Movements);
    }
}