using BusinessManager.Application.Common;
using BusinessManager.Application.Products;
using BusinessManager.Domain.Inventory;

namespace BusinessManager.Infrastructure.Tests;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task Create_WithOpeningStock_RecordsQuantityAndMovement()
    {
        await using var db = await TestDatabase.CreateAsync();

        var id = await db.AddProductAsync("water-001", 4000m, stock: 12, reorderLevel: 5);

        var product = await db.Products.GetAsync(id);
        Assert.Equal("WATER-001", product.Sku);
        Assert.Equal(12, product.QuantityOnHand);
        Assert.False(product.IsLowStock);

        var movements = await db.Products.GetMovementsAsync(id);
        var opening = Assert.Single(movements.Items);
        Assert.Equal(StockMovementType.Receipt, opening.Type);
        Assert.Equal(12, opening.QuantityAfter);
    }

    [Fact]
    public async Task Create_WithDuplicateSku_NamesExistingProduct()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Products.CreateAsync(new CreateProductRequest("WATER-001", "Bottled Water", 100m));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.CreateAsync(new CreateProductRequest(" water-001 ", "Other", 200m)));

        Assert.Contains("Bottled Water", ex.Message);
        Assert.Equal(1, (await db.Products.SearchAsync(new ProductQuery())).TotalCount);
    }

    [Theory]
    [InlineData("", "Bottled Water", 1, "SKU is required")]
    [InlineData("A B", "Bottled Water", 1, "SKU may only contain")]
    [InlineData("A1", "Bottled Water", -1, "cannot be negative")]
    [InlineData("A1", "Bottled Water", 1.234, "two decimal places")]
    public async Task Create_WithInvalidInput_GivesReadableMessage(
        string sku, string name, decimal price, string expected)
    {
        await using var db = await TestDatabase.CreateAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.CreateAsync(new CreateProductRequest(sku, name, price)));

        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Parameter", ex.Message);
    }

    [Fact]
    public async Task Search_TreatsWildcardsLiterally_AndPages()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddProductAsync("P1", 1m, name: "Juice 50% off");
        await db.AddProductAsync("P2", 1m, name: "Juice 500ml");
        for (int i = 0; i < 5; i++)
        {
            await db.AddProductAsync($"X{i}", 1m, name: $"Biscuit {i}");
        }

        var wildcard = await db.Products.SearchAsync(new ProductQuery("50%"));
        Assert.Equal("Juice 50% off", Assert.Single(wildcard.Items).Name);

        var page2 = await db.Products.SearchAsync(new ProductQuery("biscuit", Page: 2, PageSize: 2));
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(3, page2.PageCount);
        Assert.Equal(new[] { "Biscuit 2", "Biscuit 3" }, page2.Items.Select(p => p.Name));

        var absurd = await db.Products.SearchAsync(new ProductQuery(Page: int.MaxValue, PageSize: int.MaxValue));
        Assert.Equal(Paging.MaxPageSize, absurd.PageSize);
    }

    [Fact]
    public async Task LowStockFilter_ReturnsOnlyActiveProductsAtOrBelowReorderLevel()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddProductAsync("LOW", 1m, stock: 5, reorderLevel: 5);
        await db.AddProductAsync("OK", 1m, stock: 6, reorderLevel: 5);
        await db.AddProductAsync("NOALERT", 1m, stock: 0, reorderLevel: 0);
        var archived = await db.AddProductAsync("ARCH", 1m, stock: 0, reorderLevel: 5);
        await db.Products.SetActiveAsync(archived, false);

        var low = await db.Products.SearchAsync(new ProductQuery(LowStockOnly: true, IncludeArchived: true));

        Assert.Equal("LOW", Assert.Single(low.Items).Sku);
    }

    [Fact]
    public async Task FindForSale_PutsExactSkuFirst_AndExcludesArchived()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddProductAsync("AB-12", 1m, name: "Alpha");
        await db.AddProductAsync("AB-1", 1m, name: "Zulu");
        var archived = await db.AddProductAsync("AB-100", 1m, name: "Beta");
        await db.Products.SetActiveAsync(archived, false);

        var found = await db.Products.FindForSaleAsync("ab-1");

        Assert.Equal(new[] { "AB-1", "AB-12" }, found.Select(p => p.Sku));
        Assert.Empty(await db.Products.FindForSaleAsync("   "));
    }

    [Fact]
    public async Task AdjustBelowZero_IsRejectedAndChangesNothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 1m, stock: 3);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.AdjustStockAsync(id, -4, "Count"));

        Assert.Contains("negative", ex.Message);
        Assert.Equal(3, (await db.Products.GetAsync(id)).QuantityOnHand);
        Assert.Single((await db.Products.GetMovementsAsync(id)).Items);
    }

    [Fact]
    public async Task ReceiveAndAdjust_AppendHistoryNewestFirst()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 1m);

        await db.Products.ReceiveStockAsync(id, 10, "Delivery");
        await db.Products.AdjustStockAsync(id, -2, "Damaged");

        var product = await db.Products.GetAsync(id);
        Assert.Equal(8, product.QuantityOnHand);

        var history = await db.Products.GetMovementsAsync(id);
        Assert.Equal(new[] { 8, 10 }, history.Items.Select(m => m.QuantityAfter));
    }

    [Fact]
    public async Task Update_ChangesDetails_AndUnknownProductIsNotFound()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 1m);

        await db.Products.UpdateAsync(new UpdateProductRequest(id, "  Renamed  ", 2.5m, 4));

        var product = await db.Products.GetAsync(id);
        Assert.Equal("Renamed", product.Name);
        Assert.Equal(2.5m, product.SellingPrice);
        Assert.Equal(4, product.ReorderLevel);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            db.Products.UpdateAsync(new UpdateProductRequest(Guid.NewGuid(), "X", 1m, 0)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            db.Products.ReceiveStockAsync(Guid.NewGuid(), 1, "Delivery"));
    }

    [Fact]
    public async Task ReceivingBeyondIntMax_IsRejectedWithReadableMessage()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 1m, stock: int.MaxValue);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.ReceiveStockAsync(id, 1, "Delivery"));

        Assert.Contains("too large", ex.Message);
    }
}
