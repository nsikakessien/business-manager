using System.Net.Mail;
using System.Text.RegularExpressions;
using BusinessManager.Domain.Common;

namespace BusinessManager.Domain.Customers;

/// <summary>
/// A customer who may buy on account.
/// </summary>
/// <remarks>
/// <see cref="Transactions"/> holds the ledger entries recorded through this
/// instance only; the full ledger is queried separately.
/// </remarks>
public sealed partial class Customer
{
    public const int MaxNameLength = 200;
    public const int MaxPhoneLength = 30;
    public const int MaxEmailLength = 254;
    public const int MaxNotesLength = 1000;

    private readonly List<CustomerTransaction> _transactions = new();

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// The most the customer may owe. Zero means sales must be paid in full.
    /// </summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>
    /// What the customer owes. Negative means the business owes the customer
    /// (for example after a paid credit sale is voided).
    /// </summary>
    public decimal Balance { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<CustomerTransaction> Transactions => _transactions.AsReadOnly();

    public decimal AvailableCredit => Math.Max(0m, CreditLimit - Balance);

    private Customer()
    {
    }

    public Customer(
        string name,
        string? phone = null,
        string? email = null,
        string? notes = null,
        decimal creditLimit = 0m)
    {
        var details = Validate(name, phone, email, notes, creditLimit);

        Id = Guid.NewGuid();
        Apply(details);
        Balance = 0m;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateDetails(
        string name,
        string? phone,
        string? email,
        string? notes,
        decimal creditLimit)
    {
        // Lowering the limit below the current balance is allowed: it simply
        // blocks further credit until the balance is paid down.
        Apply(Validate(name, phone, email, notes, creditLimit));
    }

    public void Archive()
    {
        if (Balance != 0m)
        {
            throw new DomainException(
                "A customer with a non-zero balance cannot be archived. " +
                "Settle the account first.");
        }

        IsActive = false;
    }

    public void Restore() => IsActive = true;

    public CustomerTransaction Charge(decimal amount, Guid saleId, string description)
    {
        decimal value = Guard.Amount(amount, nameof(amount), "Charge", allowZero: false);
        RequireReference(saleId);

        if (!IsActive)
        {
            throw new DomainException(
                $"{Name} is archived and cannot buy on account.");
        }

        decimal newBalance = Balance + value;

        if (newBalance > CreditLimit)
        {
            throw new DomainException(
                $"This sale would exceed {Name}'s credit limit of " +
                $"{CreditLimit:N2}. Available credit: {AvailableCredit:N2}.");
        }

        return Record(
            CustomerTransactionType.Charge,
            value,
            paymentMethod: null,
            description,
            saleId);
    }

    public CustomerTransaction ReceivePayment(
        decimal amount,
        PaymentMethod method,
        string? description)
    {
        decimal value = Guard.Amount(amount, nameof(amount), "Payment", allowZero: false);

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Choose a payment method.");
        }

        if (Balance <= 0m)
        {
            throw new DomainException($"{Name} has no outstanding balance.");
        }

        if (value > Balance)
        {
            throw new DomainException(
                $"Payment of {value:N2} is more than the outstanding " +
                $"balance of {Balance:N2}.");
        }

        return Record(
            CustomerTransactionType.Payment,
            -value,
            method,
            string.IsNullOrWhiteSpace(description) ? "Payment received" : description,
            referenceId: null);
    }

    public CustomerTransaction ReverseCharge(decimal amount, Guid saleId, string description)
    {
        decimal value = Guard.Amount(amount, nameof(amount), "Reversal", allowZero: false);
        RequireReference(saleId);

        return Record(
            CustomerTransactionType.ChargeReversal,
            -value,
            paymentMethod: null,
            description,
            saleId);
    }

    private CustomerTransaction Record(
        CustomerTransactionType type,
        decimal signedAmount,
        PaymentMethod? paymentMethod,
        string description,
        Guid? referenceId)
    {
        string text = Guard.RequiredText(
            description,
            CustomerTransaction.MaxDescriptionLength,
            nameof(description),
            "Description");

        decimal newBalance = Balance + signedAmount;

        var transaction = new CustomerTransaction(
            Id,
            type,
            signedAmount,
            newBalance,
            paymentMethod,
            text,
            referenceId);

        _transactions.Add(transaction);
        Balance = newBalance;

        return transaction;
    }

    private static void RequireReference(Guid saleId)
    {
        if (saleId == Guid.Empty)
        {
            throw new ArgumentException("A sale ID is required.", nameof(saleId));
        }
    }

    private void Apply((string Name, string? Phone, string? Email, string? Notes, decimal CreditLimit) d)
    {
        Name = d.Name;
        Phone = d.Phone;
        Email = d.Email;
        Notes = d.Notes;
        CreditLimit = d.CreditLimit;
    }

    private static (string, string?, string?, string?, decimal) Validate(
        string name,
        string? phone,
        string? email,
        string? notes,
        decimal creditLimit)
    {
        string validName = Guard.RequiredText(name, MaxNameLength, nameof(name), "Customer name");

        string? validPhone = Guard.OptionalText(phone, MaxPhoneLength, nameof(phone), "Phone");
        if (validPhone is not null && !PhonePattern().IsMatch(validPhone))
        {
            throw new ArgumentException(
                "Phone may contain only digits, spaces, '+', '-' and brackets, " +
                "and must include at least 5 digits.",
                nameof(phone));
        }

        string? validEmail = Guard.OptionalText(email, MaxEmailLength, nameof(email), "Email");
        if (validEmail is not null &&
            (!MailAddress.TryCreate(validEmail, out var parsed) ||
             !string.Equals(parsed.Address, validEmail, StringComparison.OrdinalIgnoreCase) ||
             !validEmail.Contains('.', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Email address is not valid.", nameof(email));
        }

        string? validNotes = Guard.OptionalText(notes, MaxNotesLength, nameof(notes), "Notes");
        decimal validLimit = Guard.Amount(creditLimit, nameof(creditLimit), "Credit limit");

        return (validName, validPhone, validEmail?.ToLowerInvariant(), validNotes, validLimit);
    }

    // At least five digits, only phone punctuation.
    [GeneratedRegex(@"^(?=(?:\D*\d){5})[0-9+()\- ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
