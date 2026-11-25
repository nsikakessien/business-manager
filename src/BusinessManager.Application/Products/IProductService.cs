using BusinessManager.Application.Common;
using BusinessManager.Domain.Inventory;

namespace BusinessManager.Application.Products;

public sealed record ProductQuery(
    string? SearchText = null,
    bool IncludeArchived = false,
    bool LowStockOnly = false,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record ProductListItem(
    Guid Id,
    string Sku,
    string Name,
    decimal SellingPrice,
    int QuantityOnHand,
    int ReorderLevel,
    bool IsActive)
{
    public bool IsLowStock => IsActive && ReorderLevel > 0 && QuantityOnHand <= ReorderLevel;
    public bool IsOutOfStock => QuantityOnHand == 0;
}

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal SellingPrice,
    int ReorderLevel = 0,
    int OpeningStock = 0);

public sealed record UpdateProductRequest(
    Guid Id,
    string Name,
    decimal SellingPrice,
    int ReorderLevel);

public sealed record StockMovementItem(
    Guid Id,
    StockMovementType Type,
    int QuantityChange,
    int QuantityAfter,
    string Reason,
    Guid? ReferenceId,
    DateTimeOffset OccurredAtUtc);

public interface IProductService
{
    Task<PagedResult<ProductListItem>> SearchAsync(ProductQuery query, CancellationToken ct = default);

    Task<ProductListItem> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Active products matching a SKU or name, best matches first, for the till.</summary>
    Task<IReadOnlyList<ProductListItem>> FindForSaleAsync(string text, int maxResults = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(CreateProductRequest request, CancellationToken ct = default);

    Task UpdateAsync(UpdateProductRequest request, CancellationToken ct = default);

    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);

    Task ReceiveStockAsync(Guid productId, int quantity, string reason, CancellationToken ct = default);

    Task AdjustStockAsync(Guid productId, int quantityChange, string reason, CancellationToken ct = default);

    Task<PagedResult<StockMovementItem>> GetMovementsAsync(
        Guid productId,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default);
}
