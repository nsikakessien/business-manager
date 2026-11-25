using BusinessManager.Domain.Common;
using BusinessManager.Domain.Inventory;

namespace BusinessManager.Domain.Tests.Inventory;

public sealed class ProductLifecycleTests
{
    [Theory]
    [InlineData("-ABC")]
    [InlineData("AB C")]
    [InlineData("AB*C")]
    public void InvalidSkuCharacters_AreRejected(string sku)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Product(sku, "Name", 1m));
        Assert.Equal("sku", ex.ParamName);
    }

    [Fact]
    public void OverlongValues_AreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new Product(new string('A', Product.MaxSkuLength + 1), "Name", 1m));
        Assert.Throws<ArgumentException>(() =>
            new Product("A1", new string('n', Product.MaxNameLength + 1), 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Product("A1", "Name", 1_000_000_000_000m));
    }

    [Fact]
    public void ControlCharactersInName_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Product("A1", "Bad\0Name", 1m));
    }

    [Fact]
    public void UpdateDetails_IsAllOrNothing()
    {
        var product = new Product("A1", "Name", 1m, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.UpdateDetails("New", 2m, -1));

        Assert.Equal("Name", product.Name);
        Assert.Equal(1m, product.SellingPrice);
        Assert.Equal(5, product.ReorderLevel);
    }

    [Fact]
    public void ArchiveAndRestore_ToggleActive()
    {
        var product = new Product("A1", "Name", 1m);

        product.Archive();
        Assert.False(product.IsActive);

        product.Restore();
        Assert.True(product.IsActive);
    }

    [Fact]
    public void SaleMovements_TrackReferenceAndBalance()
    {
        var stock = new InventoryStock(Guid.NewGuid());
        var saleId = Guid.NewGuid();
        stock.Receive(5, "Delivery");

        var sold = stock.RemoveForSale(3, saleId, "Sale S-000001");

        Assert.Equal(StockMovementType.Sale, sold.Type);
        Assert.Equal(-3, sold.QuantityChange);
        Assert.Equal(2, sold.QuantityAfter);
        Assert.Equal(saleId, sold.ReferenceId);

        var ex = Assert.Throws<DomainException>(() => stock.RemoveForSale(3, saleId, "Sale"));
        Assert.Contains("Insufficient stock", ex.Message);

        var returned = stock.ReturnFromVoidedSale(3, saleId, "Void");
        Assert.Equal(5, returned.QuantityAfter);
        Assert.Equal(StockMovementType.SaleVoid, returned.Type);
    }
}
