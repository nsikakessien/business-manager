using BusinessManager.Domain.Inventory;
using Xunit;

namespace BusinessManager.Domain.Tests.Inventory;

public sealed class ProductTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesProduct()
    {
        var product = new Product(
            "WATER-001",
            "Bottled Water",
            4000m);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("WATER-001", product.Sku);
        Assert.Equal("Bottled Water", product.Name);
        Assert.Equal(4000m, product.SellingPrice);
    }

    [Fact]
    public void Constructor_NormalizesSkuAndTrimsName()
    {
        var product = new Product(
            "  water-001  ",
            "  Bottled Water  ",
            4000m);

        Assert.Equal("WATER-001", product.Sku);
        Assert.Equal("Bottled Water", product.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingSku_Throws(string? sku)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Product(sku!, "Bottled Water", 4000m));

        Assert.Equal("sku", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingName_Throws(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Product("WATER-001", name!, 4000m));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNegativePrice_Throws()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new Product("WATER-001", "Bottled Water", -1m));

        Assert.Equal("sellingPrice", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithZeroPrice_CreatesProduct()
    {
        var product = new Product(
            "SAMPLE-001",
            "Free Sample",
            0m);

        Assert.Equal(0m, product.SellingPrice);
    }

    [Fact]
    public void Constructor_WithTwoDecimalPlaces_PreservesPrice()
    {
        var product = new Product(
            "WATER-001",
            "Bottled Water",
            4000.50m);

        Assert.Equal(4000.50m, product.SellingPrice);
    }

    [Fact]
    public void Constructor_WithFractionOfSmallestCurrencyUnit_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Product("WATER-001", "Bottled Water", 4000.501m));

        Assert.Equal("sellingPrice", exception.ParamName);
    }
}