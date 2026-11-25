using BusinessManager.Application.Common;
using BusinessManager.Application.Customers;
using BusinessManager.Application.Sales;
using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;
using BusinessManager.Domain.Inventory;
using BusinessManager.Domain.Sales;

namespace BusinessManager.Infrastructure.Tests;

public sealed class SalesServiceTests
{
    [Fact]
    public async Task CashSale_ReducesStock_AndNumbersSequentially()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 250m, stock: 10);

        var first = await db.SellAsync(id, 3, 750m);
        var second = await db.SellAsync(id, 1, 250m);

        Assert.Equal("S-000001", first.Number);
        Assert.Equal("S-000002", second.Number);
        Assert.Equal(750m, first.Total);
        Assert.Equal(0m, first.AmountOnAccount);
        Assert.Equal(6, (await db.Products.GetAsync(id)).QuantityOnHand);

        var history = await db.Products.GetMovementsAsync(id);
        Assert.Contains(history.Items, m => m.Type == StockMovementType.Sale && m.ReferenceId == first.Id);
    }

    [Fact]
    public async Task Overselling_RejectsWholeSale_WithoutSideEffects()
    {
        await using var db = await TestDatabase.CreateAsync();
        var plenty = await db.AddProductAsync("A1", 10m, stock: 100);
        var scarce = await db.AddProductAsync("B1", 10m, stock: 2, name: "Scarce item");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CreateAsync(new CreateSaleRequest(
                new[] { new SaleLineRequest(plenty, 5), new SaleLineRequest(scarce, 3) },
                null, 0m, 80m, PaymentMethod.Cash, null)));

        Assert.Contains("Scarce item", ex.Message);
        Assert.Contains("Insufficient stock", ex.Message);
        Assert.Equal(100, (await db.Products.GetAsync(plenty)).QuantityOnHand);
        Assert.Equal(2, (await db.Products.GetAsync(scarce)).QuantityOnHand);
        Assert.Equal(0, (await db.Sales.SearchAsync(new SaleQuery())).TotalCount);

        // The failed attempt does not consume a sale number.
        Assert.Equal("S-000001", (await db.SellAsync(plenty, 1, 10m)).Number);
    }

    [Fact]
    public async Task DuplicateLines_AreMerged_AndCheckedAgainstStockTogether()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 10m, stock: 5);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CreateAsync(new CreateSaleRequest(
                new[] { new SaleLineRequest(id, 3), new SaleLineRequest(id, 3) },
                null, 0m, 60m, PaymentMethod.Cash, null)));

        var sale = await db.Sales.CreateAsync(new CreateSaleRequest(
            new[] { new SaleLineRequest(id, 2), new SaleLineRequest(id, 3) },
            null, 0m, 50m, PaymentMethod.Cash, null));

        var line = Assert.Single(sale.Lines);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(0, (await db.Products.GetAsync(id)).QuantityOnHand);
    }

    [Fact]
    public async Task UnpaidBalanceWithoutCustomer_IsRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 5);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 40m));

        Assert.Contains("Select a customer", ex.Message);
        Assert.Equal(5, (await db.Products.GetAsync(id)).QuantityOnHand);
    }

    [Fact]
    public async Task OverpaymentAndExcessDiscount_AreRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 5);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 150m));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 0m, discount: 101m));

        // A 100% discount is a valid free sale with no payment method.
        var free = await db.SellAsync(id, 1, 0m, discount: 100m);
        Assert.Equal(0m, free.Total);
        Assert.Null(free.PaymentMethod);
    }

    [Fact]
    public async Task CreditSale_ChargesCustomer_WithinLimitOnly()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 50);
        var customer = await db.AddCustomerAsync(creditLimit: 250m);

        var sale = await db.SellAsync(id, 3, 100m, customer);
        Assert.Equal(200m, sale.AmountOnAccount);
        Assert.Equal(200m, (await db.Customers.GetAsync(customer)).Balance);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 0m, customer));
        Assert.Contains("credit limit", ex.Message);
        Assert.Equal(47, (await db.Products.GetAsync(id)).QuantityOnHand);
        Assert.Equal(200m, (await db.Customers.GetAsync(customer)).Balance);
    }

    [Fact]
    public async Task Void_RestoresStock_ReversesCharge_AndCannotRepeat()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 10);
        var customer = await db.AddCustomerAsync(creditLimit: 1000m);
        var sale = await db.SellAsync(id, 4, 150m, customer);

        await db.Sales.VoidAsync(sale.Id, "Entered by mistake");

        Assert.Equal(10, (await db.Products.GetAsync(id)).QuantityOnHand);
        Assert.Equal(0m, (await db.Customers.GetAsync(customer)).Balance);

        var voided = await db.Sales.GetAsync(sale.Id);
        Assert.Equal(SaleStatus.Voided, voided.Status);
        Assert.Equal("Entered by mistake", voided.VoidReason);

        var ledger = await db.Customers.GetTransactionsAsync(customer);
        Assert.Equal(
            new[] { CustomerTransactionType.ChargeReversal, CustomerTransactionType.Charge },
            ledger.Items.Select(t => t.Type));

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.VoidAsync(sale.Id, "Again"));
        Assert.Contains("already been voided", again.Message);
        Assert.Equal(10, (await db.Products.GetAsync(id)).QuantityOnHand);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.VoidAsync(sale.Id, "  "));
    }

    [Fact]
    public async Task VoidingAPaidOffCreditSale_LeavesCustomerInCredit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 10);
        var customer = await db.AddCustomerAsync(creditLimit: 1000m);
        var sale = await db.SellAsync(id, 1, 0m, customer);
        await db.Customers.RecordPaymentAsync(customer, 100m, PaymentMethod.Cash, null);

        await db.Sales.VoidAsync(sale.Id, "Returned");

        var account = await db.Customers.GetAsync(customer);
        Assert.Equal(-100m, account.Balance);
    }

    [Fact]
    public async Task ArchivedProductOrCustomer_CannotBeSold()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 10);
        await db.Products.SetActiveAsync(id, false);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 100m));
        Assert.Contains("archived", ex.Message);

        await db.Products.SetActiveAsync(id, true);
        var customer = await db.AddCustomerAsync();
        await db.Customers.SetActiveAsync(customer, false);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SellAsync(id, 1, 100m, customer));
    }

    [Fact]
    public async Task UnknownProductOrCustomer_IsNotFound()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 10);

        await Assert.ThrowsAsync<NotFoundException>(() => db.SellAsync(Guid.NewGuid(), 1, 100m));
        await Assert.ThrowsAsync<NotFoundException>(() => db.SellAsync(id, 1, 0m, Guid.NewGuid()));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CreateAsync(new CreateSaleRequest(Array.Empty<SaleLineRequest>(), null, 0m, 0m, null, null)));
    }

    [Fact]
    public async Task ConcurrentSalesOfLastUnit_OnlyOneSucceeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 1);

        var attempts = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await db.SellAsync(id, 1, 100m);
                    return true;
                }
                catch (BusinessRuleException)
                {
                    return false;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r);
        Assert.Equal(0, (await db.Products.GetAsync(id)).QuantityOnHand);
        Assert.Equal(1, (await db.Sales.SearchAsync(new SaleQuery())).TotalCount);
    }

    [Fact]
    public async Task Search_FindsByNumberAndCustomer_AndFiltersVoided()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 10m, stock: 100);
        var customer = await db.Customers.CreateAsync(
            new CustomerDetailsRequest("Chidi Eze", null, null, null, 1000m));

        var s1 = await db.SellAsync(id, 1, 10m);
        await db.SellAsync(id, 1, 0m, customer);
        await db.Sales.VoidAsync(s1.Id, "Mistake");

        Assert.Equal(s1.Id, Assert.Single((await db.Sales.SearchAsync(new SaleQuery("S-000001"))).Items).Id);
        Assert.Equal(s1.Id, Assert.Single((await db.Sales.SearchAsync(new SaleQuery("1"))).Items).Id);
        Assert.Equal("Chidi Eze", Assert.Single((await db.Sales.SearchAsync(new SaleQuery("chidi"))).Items).CustomerName);
        Assert.Single((await db.Sales.SearchAsync(new SaleQuery(IncludeVoided: false))).Items);

        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Equal(2, (await db.Sales.SearchAsync(new SaleQuery(From: today, To: today))).TotalCount);
        // Reversed range is tolerated.
        Assert.Equal(2, (await db.Sales.SearchAsync(new SaleQuery(From: today, To: today.AddDays(-1)))).TotalCount);
        Assert.Equal(0, (await db.Sales.SearchAsync(new SaleQuery(From: today.AddDays(1)))).TotalCount);
    }

    [Fact]
    public async Task ExpectedTotalMismatch_IsRejectedWithoutSelling()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProductAsync("A1", 100m, stock: 5);
        await db.Products.UpdateAsync(new Application.Products.UpdateProductRequest(id, "A1", 120m, 0));

        var ex = await Assert.ThrowsAsync<PriceChangedException>(() =>
            db.Sales.CreateAsync(new CreateSaleRequest(
                new[] { new SaleLineRequest(id, 1) }, null, 0m, 100m, PaymentMethod.Cash, null, ExpectedTotal: 100m)));

        Assert.Equal(120m, ex.CurrentTotal);
        Assert.Equal(5, (await db.Products.GetAsync(id)).QuantityOnHand);
    }
}
