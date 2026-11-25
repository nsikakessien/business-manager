namespace BusinessManager.Domain.Customers;

public enum CustomerTransactionType
{
    /// <summary>Goods sold on account (increases what the customer owes).</summary>
    Charge = 1,

    /// <summary>Money received from the customer (decreases what they owe).</summary>
    Payment = 2,

    /// <summary>A charge cancelled because its sale was voided.</summary>
    ChargeReversal = 3
}
