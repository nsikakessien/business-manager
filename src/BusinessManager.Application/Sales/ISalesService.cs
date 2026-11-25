using BusinessManager.Application.Common;
using BusinessManager.Domain.Common;
using BusinessManager.Domain.Sales;

namespace BusinessManager.Application.Sales;

public sealed record SaleLineRequest(Guid ProductId, int Quantity);

public sealed record CreateSaleRequest(
    IReadOnlyList<SaleLineRequest> Lines,
    Guid? CustomerId,
    decimal Discount,
    decimal AmountPaid,
    PaymentMethod? PaymentMethod,
    string? Notes,
    decimal? ExpectedTotal = null);

public sealed record SaleQuery(
    string? SearchText = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool IncludeVoided = true,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record SaleListItem(
    Guid Id,
    string Number,
    DateTimeOffset OccurredAtUtc,
    string? CustomerName,
    decimal Total,
    decimal AmountPaid,
    decimal AmountOnAccount,
    SaleStatus Status);

public sealed record SaleLineItem(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record SaleDetails(
    Guid Id,
    string Number,
    DateTimeOffset OccurredAtUtc,
    Guid? CustomerId,
    string? CustomerName,
    IReadOnlyList<SaleLineItem> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal AmountPaid,
    decimal AmountOnAccount,
    PaymentMethod? PaymentMethod,
    string? Notes,
    SaleStatus Status,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason);

public interface ISalesService
{
    Task<SaleDetails> CreateAsync(CreateSaleRequest request, CancellationToken ct = default);

    Task VoidAsync(Guid saleId, string reason, CancellationToken ct = default);

    Task<SaleDetails> GetAsync(Guid saleId, CancellationToken ct = default);

    Task<PagedResult<SaleListItem>> SearchAsync(SaleQuery query, CancellationToken ct = default);
}
