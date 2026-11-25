using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;

namespace BusinessManager.Domain.Tests.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void Constructor_NormalisesOptionalFields()
    {
        var customer = new Customer("  Ada  ", "   ", " Ada@Example.COM ", "", 500m);

        Assert.Equal("Ada", customer.Name);
        Assert.Null(customer.Phone);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Null(customer.Notes);
        Assert.Equal(0m, customer.Balance);
        Assert.True(customer.IsActive);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("call me")]
    [InlineData("0803-ABC-1234")]
    public void Constructor_WithInvalidPhone_Throws(string phone)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Customer("Ada", phone));
        Assert.Equal("phone", ex.ParamName);
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("ada@localhost")]
    [InlineData("Ada <ada@example.com>")]
    public void Constructor_WithInvalidEmail_Throws(string email)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Customer("Ada", email: email));
        Assert.Equal("email", ex.ParamName);
    }

    [Fact]
    public void Charge_UpToCreditLimit_IsAllowed_ButNotBeyond()
    {
        var customer = new Customer("Ada", creditLimit: 100m);
        var saleId = Guid.NewGuid();

        customer.Charge(60m, saleId, "Sale 1");
        customer.Charge(40m, saleId, "Sale 2");

        Assert.Equal(100m, customer.Balance);
        Assert.Equal(0m, customer.AvailableCredit);

        Assert.Throws<DomainException>(() => customer.Charge(0.01m, saleId, "Sale 3"));
        Assert.Equal(100m, customer.Balance);
        Assert.Equal(2, customer.Transactions.Count);
    }

    [Fact]
    public void Charge_WithZeroLimit_IsRejected()
    {
        var customer = new Customer("Ada");

        Assert.Throws<DomainException>(() => customer.Charge(1m, Guid.NewGuid(), "Sale"));
    }

    [Fact]
    public void Payment_CannotExceedBalance_OrBeTakenWhenNothingOwed()
    {
        var customer = new Customer("Ada", creditLimit: 100m);

        Assert.Throws<DomainException>(() => customer.ReceivePayment(10m, PaymentMethod.Cash, null));

        customer.Charge(50m, Guid.NewGuid(), "Sale");

        Assert.Throws<DomainException>(() => customer.ReceivePayment(50.01m, PaymentMethod.Cash, null));

        var payment = customer.ReceivePayment(50m, PaymentMethod.Card, null);

        Assert.Equal(-50m, payment.Amount);
        Assert.Equal(0m, payment.BalanceAfter);
        Assert.Equal("Payment received", payment.Description);
        Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
    }

    [Fact]
    public void Archive_RequiresZeroBalance_AndBlocksCredit()
    {
        var customer = new Customer("Ada", creditLimit: 100m);
        customer.Charge(10m, Guid.NewGuid(), "Sale");

        Assert.Throws<DomainException>(customer.Archive);

        customer.ReceivePayment(10m, PaymentMethod.Cash, null);
        customer.Archive();

        Assert.False(customer.IsActive);
        Assert.Throws<DomainException>(() => customer.Charge(1m, Guid.NewGuid(), "Sale"));
    }

    [Fact]
    public void LoweringLimitBelowBalance_IsAllowed_AndBlocksFurtherCredit()
    {
        var customer = new Customer("Ada", creditLimit: 100m);
        customer.Charge(80m, Guid.NewGuid(), "Sale");

        customer.UpdateDetails("Ada", null, null, null, 50m);

        Assert.Equal(0m, customer.AvailableCredit);
        Assert.Throws<DomainException>(() => customer.Charge(1m, Guid.NewGuid(), "Sale"));
    }

    [Fact]
    public void FailedUpdate_LeavesDetailsUnchanged()
    {
        var customer = new Customer("Ada", "08031234567", creditLimit: 100m);

        Assert.Throws<ArgumentException>(() =>
            customer.UpdateDetails("New name", "08031234567", "bad", null, 200m));

        Assert.Equal("Ada", customer.Name);
        Assert.Equal(100m, customer.CreditLimit);
    }
}
