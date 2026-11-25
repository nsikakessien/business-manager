using BusinessManager.Application.Common;
using BusinessManager.Application.Products;
using BusinessManager.Domain.Inventory;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Services;

public sealed class ProductService : IProductService
{
    private const int MaxFindResults = 50;

    private readonly IDbContextFactory<BusinessManagerDbContext> _factory;

    public ProductService(IDbContextFactory<BusinessManagerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<PagedResult<ProductListItem>> SearchAsync(
        ProductQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, size, skip) = Paging.Normalize(query.Page, query.PageSize);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = Rows(db);

        if (!query.IncludeArchived)
        {
            rows = rows.Where(r => r.Product.IsActive);
        }

        if (LikePattern.Contains(query.SearchText) is { } pattern)
        {
            rows = rows.Where(r =>
                EF.Functions.Like(r.Product.Sku, pattern, LikePattern.Escape) ||
                EF.Functions.Like(r.Product.Name, pattern, LikePattern.Escape));
        }

        if (query.LowStockOnly)
        {
            rows = rows.Where(r =>
                r.Product.IsActive &&
                r.Product.ReorderLevel > 0 &&
                r.Quantity <= r.Product.ReorderLevel);
        }

        int total = await rows.CountAsync(ct);

        var list = await rows
            .OrderBy(r => r.Product.Name)
            .ThenBy(r => r.Product.Sku)
            .Skip(skip)
            .Take(size)
            .Select(ToItem)
            .ToListAsync(ct);

        return new PagedResult<ProductListItem>(list, total, page, size);
    }

    public async Task<ProductListItem> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await Rows(db)
                   .Where(r => r.Product.Id == id)
                   .Select(ToItem)
                   .SingleOrDefaultAsync(ct)
               ?? throw new NotFoundException("The product");
    }

    public async Task<IReadOnlyList<ProductListItem>> FindForSaleAsync(
        string text,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        string? term = LikePattern.Normalize(text);
        if (term is null)
        {
            return Array.Empty<ProductListItem>();
        }

        int take = Math.Clamp(maxResults, 1, MaxFindResults);
        string upper = term.ToUpperInvariant();
        string contains = LikePattern.Contains(term)!;
        string startsWith = LikePattern.StartsWith(upper)!;

        await using var db = await _factory.CreateDbContextAsync(ct);

        return await Rows(db)
            .Where(r => r.Product.IsActive)
            .Where(r =>
                EF.Functions.Like(r.Product.Sku, contains, LikePattern.Escape) ||
                EF.Functions.Like(r.Product.Name, contains, LikePattern.Escape))
            // Exact SKU (e.g. from a barcode scanner) first, then SKU prefix, then by name.
            .OrderBy(r => r.Product.Sku == upper
                ? 0
                : EF.Functions.Like(r.Product.Sku, startsWith, LikePattern.Escape) ? 1 : 2)
            .ThenBy(r => r.Product.Name)
            .Take(take)
            .Select(ToItem)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OpeningStock < 0)
        {
            throw new BusinessRuleException("Opening stock cannot be negative.");
        }

        var product = ErrorTranslation.Domain(() => new Product(
            request.Sku,
            request.Name,
            request.SellingPrice,
            request.ReorderLevel));

        var stock = new InventoryStock(product.Id);

        if (request.OpeningStock > 0)
        {
            ErrorTranslation.Domain(() => stock.Receive(request.OpeningStock, "Opening stock"));
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.Products
            .AsNoTracking()
            .Where(p => p.Sku == product.Sku)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            throw DuplicateSku(product.Sku, existing);
        }

        db.Products.Add(product);
        db.Stocks.Add(stock);

        await ErrorTranslation.SaveAsync(db, ct, ex =>
            ErrorTranslation.IsUniqueViolation(ex) ? DuplicateSku(product.Sku, null) : null);

        await tx.CommitAsync(ct);
        return product.Id;
    }

    public async Task UpdateAsync(UpdateProductRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException("The product");

        ErrorTranslation.Domain(() =>
            product.UpdateDetails(request.Name, request.SellingPrice, request.ReorderLevel));

        await ErrorTranslation.SaveAsync(db, ct);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("The product");

        if (isActive)
        {
            product.Restore();
        }
        else
        {
            product.Archive();
        }

        await ErrorTranslation.SaveAsync(db, ct);
    }

    public Task ReceiveStockAsync(
        Guid productId,
        int quantity,
        string reason,
        CancellationToken ct = default) =>
        ChangeStockAsync(productId, stock => stock.Receive(quantity, reason), ct);

    public Task AdjustStockAsync(
        Guid productId,
        int quantityChange,
        string reason,
        CancellationToken ct = default) =>
        ChangeStockAsync(productId, stock => stock.Adjust(quantityChange, reason), ct);

    public async Task<PagedResult<StockMovementItem>> GetMovementsAsync(
        Guid productId,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var (number, size, skip) = Paging.Normalize(page, pageSize);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var query = db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId);

        int total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.OccurredAtUtc)
            .Skip(skip)
            .Take(size)
            .Select(m => new StockMovementItem(
                m.Id,
                m.Type,
                m.QuantityChange,
                m.QuantityAfter,
                m.Reason,
                m.ReferenceId,
                m.OccurredAtUtc))
            .ToListAsync(ct);

        return new PagedResult<StockMovementItem>(items, total, number, size);
    }

    private async Task ChangeStockAsync(
        Guid productId,
        Action<InventoryStock> change,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // IMMEDIATE transaction: the read-modify-write of the quantity cannot
        // interleave with another writer.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        bool productExists = await db.Products.AnyAsync(p => p.Id == productId, ct);
        if (!productExists)
        {
            throw new NotFoundException("The product");
        }

        var stock = await db.Stocks.SingleOrDefaultAsync(s => s.ProductId == productId, ct);
        if (stock is null)
        {
            stock = new InventoryStock(productId);
            db.Stocks.Add(stock);
        }

        ErrorTranslation.Domain(() => change(stock));

        await ErrorTranslation.SaveAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    private sealed class ProductRow
    {
        public required Product Product { get; init; }
        public int Quantity { get; init; }
    }

    private static IQueryable<ProductRow> Rows(BusinessManagerDbContext db) =>
        from p in db.Products.AsNoTracking()
        join s in db.Stocks.AsNoTracking() on p.Id equals s.ProductId into stocks
        from s in stocks.DefaultIfEmpty()
        select new ProductRow { Product = p, Quantity = s == null ? 0 : s.QuantityOnHand };

    private static readonly System.Linq.Expressions.Expression<Func<ProductRow, ProductListItem>> ToItem =
        r => new ProductListItem(
            r.Product.Id,
            r.Product.Sku,
            r.Product.Name,
            r.Product.SellingPrice,
            r.Quantity,
            r.Product.ReorderLevel,
            r.Product.IsActive);

    private static BusinessRuleException DuplicateSku(string sku, string? existingName) =>
        new(existingName is null
            ? $"SKU {sku} is already in use."
            : $"SKU {sku} is already used by \"{existingName}\".");
}
