using BusinessManager.Application.Common;
using BusinessManager.Application.Customers;
using BusinessManager.Domain.Common;

namespace BusinessManager.Infrastructure.Tests;

public sealed class CustomerAndDashboardTests
{
    [Fact]
    public async Task Payment_ReducesBalance_AndOverpaymentIsRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var product = await db.AddProductAsync("A1", 100m, stock: 10);
        var customer = await db.AddCustomerAsync(creditLimit: 500m);
        await db.SellAsync(product, 3, 0m, customer);

        await db.Customers.RecordPaymentAsync(customer, 120.50m, PaymentMethod.BankTransfer, "Part payment");

        Assert.Equal(179.50m, (await db.Customers.GetAsync(customer)).Balance);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Customers.RecordPaymentAsync(customer, 200m, PaymentMethod.Cash, null));
        Assert.Contains("more than the outstanding balance", ex.Message);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Customers.RecordPaymentAsync(customer, 0m, PaymentMethod.Cash, null));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Customers.RecordPaymentAsync(customer, 1m, (PaymentMethod)99, null));

        Assert.Equal(179.50m, (await db.Customers.GetAsync(customer)).Balance);
    }

    [Fact]
    public async Task Archive_IsBlockedWhileBalanceOutstanding()
    {
        await using var db = await TestDatabase.CreateAsync();
        var product = await db.AddProductAsync("A1", 100m, stock: 10);
        var customer = await db.AddCustomerAsync(creditLimit: 500m);
        await db.SellAsync(product, 1, 0m, customer);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Customers.SetActiveAsync(customer, false));

        await db.Customers.RecordPaymentAsync(customer, 100m, PaymentMethod.Cash, null);
        await db.Customers.SetActiveAsync(customer, false);

        Assert.False((await db.Customers.GetAsync(customer)).IsActive);
    }

    [Fact]
    public async Task Search_MatchesNamePhoneEmail_AndBalanceFilter()
    {
        await using var db = await TestDatabase.CreateAsync();
        var product = await db.AddProductAsync("A1", 100m, stock: 10);
        var owing = await db.Customers.CreateAsync(
            new CustomerDetailsRequest("Bola Ade", "+234 803 555 0101", "Bola@Example.com", null, 1000m));
        await db.Customers.CreateAsync(
            new CustomerDetailsRequest("Musa Bello", null, null, null, 0m));
        await db.SellAsync(product, 1, 0m, owing);

        Assert.Equal("Bola Ade", Assert.Single((await db.Customers.SearchAsync(new CustomerQuery("555"))).Items).Name);
        Assert.Equal("bola@example.com", Assert.Single((await db.Customers.SearchAsync(new CustomerQuery("example"))).Items).Email);
        Assert.Equal(2, (await db.Customers.SearchAsync(new CustomerQuery())).TotalCount);
        Assert.Equal("Bola Ade", Assert.Single((await db.Customers.SearchAsync(new CustomerQuery(WithBalanceOnly: true))).Items).Name);
    }

    [Theory]
    [InlineData("", null, null, "is required")]
    [InlineData("Ade", "abc", null, "Phone")]
    [InlineData("Ade", null, "not-an-email", "Email")]
    public async Task Create_WithInvalidDetails_IsRejected(string name, string? phone, string? email, string expected)
    {
        await using var db = await TestDatabase.CreateAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Customers.CreateAsync(new CustomerDetailsRequest(name, phone, email, null, 0m)));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Dashboard_SummarisesTodayStockAndReceivables_ExcludingVoids()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await db.AddProductAsync("A1", 100m, stock: 10, reorderLevel: 9);
        await db.AddProductAsync("B1", 50m, stock: 0);
        var customer = await db.AddCustomerAsync(creditLimit: 1000m);

        await db.SellAsync(a, 1, 100m);
        await db.SellAsync(a, 2, 50m, customer);
        var voided = await db.SellAsync(a, 1, 100m);
        await db.Sales.VoidAsync(voided.Id, "Mistake");

        var summary = await db.Dashboard.GetSummaryAsync();

        Assert.Equal(2, summary.SalesToday);
        Assert.Equal(300m, summary.RevenueToday);
        Assert.Equal(300m, summary.Revenue7Days);
        Assert.Equal(7, summary.Last7Days.Count);
        Assert.Equal(2, summary.ActiveProducts);
        Assert.Equal(1, summary.LowStockProducts);
        Assert.Equal(1, summary.OutOfStockProducts);
        Assert.Equal(150m, summary.TotalReceivables);
        Assert.Equal(1, summary.CustomersOwing);
    }
}
