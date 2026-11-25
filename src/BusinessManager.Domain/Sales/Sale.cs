using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;
using BusinessManager.Domain.Inventory;

namespace BusinessManager.Domain.Sales;

/// <summary>
/// A completed sale. Any part of the total not paid at the till is charged to
/// the customer's account.
/// </summary>
/// <remarks>
/// The sale validates its own figures; adjusting stock and the customer's
/// account is coordinated by the application service in the same transaction.
/// </remarks>
public sealed class Sale
{
    public const int MaxLines = 200;
    public const int MaxLineQuantity = 1_000_000;
    public const int MaxNotesLength = 500;
    public const int MaxVoidReasonLength = 500;

    private readonly List<SaleLine> _lines = new();

    public Guid Id { get; private set; }

    /// <summary>Sequential number shown to people, e.g. S-000042.</summary>
    public long Sequence { get; private set; }

    public string Number => FormatNumber(Sequence);

    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string? CustomerName { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }

    /// <summary>The part of the total charged to the customer's account.</summary>
    public decimal AmountOnAccount { get; private set; }

    public PaymentMethod? PaymentMethod { get; private set; }
    public string? Notes { get; private set; }
    public SaleStatus Status { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();

    private Sale()
    {
    }

    public Sale(
        long sequence,
        IReadOnlyCollection<(Product Product, int Quantity)> items,
        Customer? customer,
        decimal discount,
        decimal amountPaid,
        PaymentMethod? paymentMethod,
        string? notes)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sale number must be positive.");
        }

        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new DomainException("Add at least one product to the sale.");
        }

        Id = Guid.NewGuid();

        // The same product entered twice becomes one line.
        var merged = new Dictionary<Guid, (Product Product, int Quantity)>();

        foreach (var (product, quantity) in items)
        {
            ArgumentNullException.ThrowIfNull(product, nameof(items));

            if (quantity <= 0)
            {
                throw new DomainException(
                    $"Quantity for {product.Name} must be greater than zero.");
            }

            int combined = merged.TryGetValue(product.Id, out var existing)
                ? checked(existing.Quantity + quantity)
                : quantity;

            if (combined > MaxLineQuantity)
            {
                throw new DomainException(
                    $"Quantity for {product.Name} cannot exceed {MaxLineQuantity:N0}.");
            }

            if (!product.IsActive)
            {
                throw new DomainException($"{product.Name} is archived and cannot be sold.");
            }

            merged[product.Id] = (product, combined);
        }

        if (merged.Count > MaxLines)
        {
            throw new DomainException($"A sale cannot have more than {MaxLines} lines.");
        }

        foreach (var (product, quantity) in merged.Values)
        {
            _lines.Add(new SaleLine(
                Id,
                product.Id,
                product.Sku,
                product.Name,
                product.SellingPrice,
                quantity));
        }

        decimal subtotal = _lines.Sum(line => line.LineTotal);

        if (subtotal > Guard.MaxAmount)
        {
            throw new DomainException("The sale total is too large.");
        }

        decimal validDiscount = Guard.Amount(discount, nameof(discount), "Discount");

        if (validDiscount > subtotal)
        {
            throw new DomainException(
                $"Discount ({validDiscount:N2}) cannot exceed the subtotal ({subtotal:N2}).");
        }

        decimal total = subtotal - validDiscount;
        decimal paid = Guard.Amount(amountPaid, nameof(amountPaid), "Amount paid");

        if (paid > total)
        {
            throw new DomainException(
                $"Amount paid ({paid:N2}) cannot exceed the total ({total:N2}). " +
                "Give change for any excess.");
        }

        if (paid > 0m)
        {
            if (paymentMethod is null || !Enum.IsDefined(paymentMethod.Value))
            {
                throw new DomainException("Choose how the customer paid.");
            }
        }
        else
        {
            paymentMethod = null;
        }

        decimal onAccount = total - paid;

        if (onAccount > 0m && customer is null)
        {
            throw new DomainException(
                $"{onAccount:N2} is unpaid. Select a customer to put it on their account, " +
                "or take full payment.");
        }

        if (customer is not null && !customer.IsActive)
        {
            throw new DomainException($"{customer.Name} is archived.");
        }

        Sequence = sequence;
        OccurredAtUtc = DateTimeOffset.UtcNow;
        CustomerId = customer?.Id;
        CustomerName = customer?.Name;
        Subtotal = subtotal;
        Discount = validDiscount;
        Total = total;
        AmountPaid = paid;
        AmountOnAccount = onAccount;
        PaymentMethod = paymentMethod;
        Notes = Guard.OptionalText(notes, MaxNotesLength, nameof(notes), "Notes");
        Status = SaleStatus.Completed;
    }

    public void Void(string reason)
    {
        string validReason = Guard.RequiredText(
            reason,
            MaxVoidReasonLength,
            nameof(reason),
            "Void reason");

        if (Status == SaleStatus.Voided)
        {
            throw new DomainException($"Sale {Number} has already been voided.");
        }

        Status = SaleStatus.Voided;
        VoidReason = validReason;
        VoidedAtUtc = DateTimeOffset.UtcNow;
    }

    public static string FormatNumber(long sequence) => $"S-{sequence:D6}";
}
