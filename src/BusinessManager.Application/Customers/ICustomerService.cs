using BusinessManager.Application.Common;
using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;

namespace BusinessManager.Application.Customers;

public sealed record CustomerQuery(
    string? SearchText = null,
    bool IncludeArchived = false,
    bool WithBalanceOnly = false,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record CustomerListItem(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    decimal CreditLimit,
    decimal Balance,
    bool IsActive)
{
    public decimal AvailableCredit => Math.Max(0m, CreditLimit - Balance);
}

public sealed record CustomerDetailsRequest(
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    decimal CreditLimit);

public sealed record CustomerTransactionItem(
    Guid Id,
    CustomerTransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    PaymentMethod? PaymentMethod,
    string Description,
    Guid? ReferenceId,
    DateTimeOffset OccurredAtUtc);

public interface ICustomerService
{
    Task<PagedResult<CustomerListItem>> SearchAsync(CustomerQuery query, CancellationToken ct = default);

    Task<CustomerListItem> GetAsync(Guid id, CancellationToken ct = default);

    Task<Guid> CreateAsync(CustomerDetailsRequest request, CancellationToken ct = default);

    Task UpdateAsync(Guid id, CustomerDetailsRequest request, CancellationToken ct = default);

    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);

    Task RecordPaymentAsync(
        Guid customerId,
        decimal amount,
        PaymentMethod method,
        string? note,
        CancellationToken ct = default);

    Task<PagedResult<CustomerTransactionItem>> GetTransactionsAsync(
        Guid customerId,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default);
}
