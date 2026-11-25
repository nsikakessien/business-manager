using BusinessManager.Domain.Common;

namespace BusinessManager.Domain.Customers;

/// <summary>
/// An immutable entry in a customer's account ledger.
/// </summary>
public sealed class CustomerTransaction
{
    public const int MaxDescriptionLength = 500;

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public CustomerTransactionType Type { get; private set; }

    /// <summary>
    /// Signed change to the balance: positive increases what the customer owes.
    /// </summary>
    public decimal Amount { get; private set; }

    public decimal BalanceAfter { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid? ReferenceId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private CustomerTransaction()
    {
    }

    internal CustomerTransaction(
        Guid customerId,
        CustomerTransactionType type,
        decimal amount,
        decimal balanceAfter,
        PaymentMethod? paymentMethod,
        string description,
        Guid? referenceId)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        Type = type;
        Amount = amount;
        BalanceAfter = balanceAfter;
        PaymentMethod = paymentMethod;
        Description = description;
        ReferenceId = referenceId;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }
}
