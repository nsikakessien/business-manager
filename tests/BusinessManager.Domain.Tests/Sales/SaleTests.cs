using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;
using BusinessManager.Domain.Inventory;
using BusinessManager.Domain.Sales;

namespace BusinessManager.Domain.Tests.Sales;

public sealed class SaleTests
{
    private static readonly Product Water = new("WATER", "Water", 150m);
    private static readonly Product Bread = new("BREAD", "Bread", 1200.50m);

    private static Sale Create(
        IReadOnlyCollection<(Product, int)> items,
        Customer? customer = null,
        decimal discount = 0m,
        decimal? paid = null,
        PaymentMethod? method = PaymentMethod.Cash)
    {
        decimal total = items.Sum(i => i.Item1.SellingPrice * i.Item2) - discount;
        return new Sale(1, items, customer, discount, paid ?? total, method, null);
    }

    [Fact]
    public void Totals_AreComputedFromLines()
    {
        var sale = Create(new[] { (Water, 3), (Bread, 2) }, discount: 1m);

        Assert.Equal(2, sale.Lines.Count);
        Assert.Equal(450m + 2401m, sale.Subtotal);
        Assert.Equal(2850m, sale.Total);
        Assert.Equal(2850m, sale.AmountPaid);
        Assert.Equal(0m, sale.AmountOnAccount);
        Assert.Equal("S-000001", sale.Number);
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [Fact]
    public void LinesSnapshotProductDetails()
    {
        var product = new Product("SNAP", "Original", 10m);
        var sale = Create(new[] { (product, 1) });

        product.UpdateDetails("Renamed", 99m, 0);

        var line = Assert.Single(sale.Lines);
        Assert.Equal("Original", line.ProductName);
        Assert.Equal(10m, line.UnitPrice);
    }

    [Fact]
    public void DuplicateProducts_AreMergedIntoOneLine()
    {
        var sale = Create(new[] { (Water, 1), (Water, 2) });

        var line = Assert.Single(sale.Lines);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(450m, line.LineTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Sale.MaxLineQuantity + 1)]
    public void InvalidQuantity_IsRejected(int quantity)
    {
        Assert.Throws<DomainException>(() =>
            new Sale(1, new[] { (Water, quantity) }, null, 0m, 0m, null, null));
    }

    [Fact]
    public void EmptySale_IsRejected()
    {
        Assert.Throws<DomainException>(() =>
            new Sale(1, Array.Empty<(Product, int)>(), null, 0m, 0m, null, null));
    }

    [Fact]
    public void PartialPayment_RequiresCustomer()
    {
        Assert.Throws<DomainException>(() => Create(new[] { (Water, 1) }, paid: 100m));

        var customer = new Customer("Ada", creditLimit: 1000m);
        var sale = Create(new[] { (Water, 1) }, customer, paid: 100m);

        Assert.Equal(50m, sale.AmountOnAccount);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal("Ada", sale.CustomerName);
    }

    [Fact]
    public void PaymentWithoutMethod_IsRejected_AndZeroPaymentClearsMethod()
    {
        Assert.Throws<DomainException>(() => Create(new[] { (Water, 1) }, method: null));

        var customer = new Customer("Ada", creditLimit: 1000m);
        var sale = Create(new[] { (Water, 1) }, customer, paid: 0m, method: PaymentMethod.Card);

        Assert.Null(sale.PaymentMethod);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(151)]
    public void InvalidDiscount_IsRejected(decimal discount)
    {
        Assert.ThrowsAny<Exception>(() =>
            new Sale(1, new[] { (Water, 1) }, null, discount, 0m, null, null));
    }

    [Fact]
    public void ArchivedProduct_CannotBeSold()
    {
        var product = new Product("OLD", "Old", 1m);
        product.Archive();

        Assert.Throws<DomainException>(() => Create(new[] { (product, 1) }));
    }

    [Fact]
    public void Void_RequiresReason_AndHappensOnce()
    {
        var sale = Create(new[] { (Water, 1) });

        Assert.Throws<ArgumentException>(() => sale.Void("  "));
        Assert.Equal(SaleStatus.Completed, sale.Status);

        sale.Void("  Customer changed mind ");

        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal("Customer changed mind", sale.VoidReason);
        Assert.NotNull(sale.VoidedAtUtc);
        Assert.Throws<DomainException>(() => sale.Void("Again"));
    }
}
