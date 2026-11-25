using BusinessManager.Application.Common;
using BusinessManager.Application.Sales;
using BusinessManager.Domain.Customers;
using BusinessManager.Domain.Inventory;
using BusinessManager.Domain.Sales;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Services;

public sealed class SalesService : ISalesService
{
    /// <summary>Upper bound on raw request lines before duplicates are merged.</summary>
    private const int MaxRequestLines = Sale.MaxLines * 5;

    private readonly IDbContextFactory<BusinessManagerDbContext> _factory;
    private readonly TimeProvider _time;

    public SalesService(
        IDbContextFactory<BusinessManagerDbContext> factory,
        TimeProvider? time = null)
    {
        _factory = factory;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SaleDetails> CreateAsync(CreateSaleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new BusinessRuleException("Add at least one product to the sale.");
        }

        if (request.Lines.Count > MaxRequestLines)
        {
            throw new BusinessRuleException($"A sale cannot have more than {Sale.MaxLines} lines.");
        }

        if (request.Lines.Any(l => l is null || l.ProductId == Guid.Empty))
        {
            throw new BusinessRuleException("Every sale line must reference a product.");
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();

        await using var db = await _factory.CreateDbContextAsync(ct);

        // IMMEDIATE transaction: stock levels, the customer balance and the
        // sale number are read and written atomically, so two tills can never
        // sell the same last unit or issue the same sale number.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != productIds.Count)
        {
            throw new NotFoundException("A product in this sale");
        }

        var stocks = await db.Stocks
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, ct);

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException("The customer");
        }

        long sequence = (await db.Sales.MaxAsync(s => (long?)s.Sequence, ct) ?? 0L) + 1;

        var items = request.Lines
            .Select(l => (products[l.ProductId], l.Quantity))
            .ToList();

        if (request.ExpectedTotal is { } expected)
        {
            // The till showed a total based on the prices it loaded. If a price
            // changed meanwhile, never charge an amount the customer wasn't shown.
            decimal current = items.Sum(i => i.Item1.SellingPrice * Math.Max(0, i.Quantity)) - request.Discount;
            if (current != expected)
            {
                throw new PriceChangedException(current);
            }
        }

        var sale = ErrorTranslation.Domain(() => new Sale(
            sequence,
            items,
            customer,
            request.Discount,
            request.AmountPaid,
            request.PaymentMethod,
            request.Notes));

        foreach (var line in sale.Lines)
        {
            if (!stocks.TryGetValue(line.ProductId, out var stock))
            {
                stock = new InventoryStock(line.ProductId);
                db.Stocks.Add(stock);
                stocks[line.ProductId] = stock;
            }

            ErrorTranslation.Domain(
                () => stock.RemoveForSale(line.Quantity, sale.Id, $"Sale {sale.Number}"),
                line.ProductName);
        }

        if (sale.AmountOnAccount > 0m)
        {
            ErrorTranslation.Domain(() =>
                customer!.Charge(sale.AmountOnAccount, sale.Id, $"Sale {sale.Number}"));
        }

        db.Sales.Add(sale);

        await ErrorTranslation.SaveAsync(db, ct, ex =>
            ErrorTranslation.IsUniqueViolation(ex) ? new ConcurrencyConflictException(ex) : null);

        await tx.CommitAsync(ct);

        return ToDetails(sale);
    }

    public async Task VoidAsync(Guid saleId, string reason, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sale = await db.Sales
                       .Include(s => s.Lines)
                       .SingleOrDefaultAsync(s => s.Id == saleId, ct)
                   ?? throw new NotFoundException("The sale");

        ErrorTranslation.Domain(() => sale.Void(reason));

        var productIds = sale.Lines.Select(l => l.ProductId).ToList();
        var stocks = await db.Stocks
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, ct);

        foreach (var line in sale.Lines)
        {
            if (!stocks.TryGetValue(line.ProductId, out var stock))
            {
                stock = new InventoryStock(line.ProductId);
                db.Stocks.Add(stock);
                stocks[line.ProductId] = stock;
            }

            ErrorTranslation.Domain(
                () => stock.ReturnFromVoidedSale(line.Quantity, sale.Id, $"Void of sale {sale.Number}"),
                line.ProductName);
        }

        if (sale.AmountOnAccount > 0m && sale.CustomerId is { } customerId)
        {
            var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
                           ?? throw new NotFoundException("The sale's customer");

            ErrorTranslation.Domain(() =>
                customer.ReverseCharge(sale.AmountOnAccount, sale.Id, $"Void of sale {sale.Number}"));
        }

        await ErrorTranslation.SaveAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<SaleDetails> GetAsync(Guid saleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var sale = await db.Sales
                       .AsNoTracking()
                       .Include(s => s.Lines)
                       .SingleOrDefaultAsync(s => s.Id == saleId, ct)
                   ?? throw new NotFoundException("The sale");

        return ToDetails(sale);
    }

    public async Task<PagedResult<SaleListItem>> SearchAsync(SaleQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, size, skip) = Paging.Normalize(query.Page, query.PageSize);

        await using var db = await _factory.CreateDbContextAsync(ct);

        IQueryable<Sale> sales = db.Sales.AsNoTracking();

        if (!query.IncludeVoided)
        {
            sales = sales.Where(s => s.Status == SaleStatus.Completed);
        }

        var (from, to) = (query.From, query.To);
        if (from is not null && to is not null && from > to)
        {
            (from, to) = (to, from);
        }

        if (from is { } fromDate)
        {
            var start = LocalDayStartUtc(fromDate);
            sales = sales.Where(s => s.OccurredAtUtc >= start);
        }

        if (to is { } toDate && toDate < DateOnly.MaxValue)
        {
            var end = LocalDayStartUtc(toDate.AddDays(1));
            sales = sales.Where(s => s.OccurredAtUtc < end);
        }

        if (LikePattern.Normalize(query.SearchText) is { } term)
        {
            string pattern = LikePattern.Contains(term)!;

            // "S-000042", "000042" and "42" all find sale 42.
            string digits = term.StartsWith("S-", StringComparison.OrdinalIgnoreCase) ? term[2..] : term;
            long? sequence = long.TryParse(digits, out long n) && n > 0 ? n : null;

            sales = sales.Where(s =>
                (sequence != null && s.Sequence == sequence) ||
                (s.CustomerName != null && EF.Functions.Like(s.CustomerName, pattern, LikePattern.Escape)));
        }

        int total = await sales.CountAsync(ct);

        var rows = await sales
            .OrderByDescending(s => s.Sequence)
            .Skip(skip)
            .Take(size)
            .Select(s => new
            {
                s.Id,
                s.Sequence,
                s.OccurredAtUtc,
                s.CustomerName,
                s.Total,
                s.AmountPaid,
                s.AmountOnAccount,
                s.Status
            })
            .ToListAsync(ct);

        var items = rows
            .Select(s => new SaleListItem(
                s.Id,
                Sale.FormatNumber(s.Sequence),
                s.OccurredAtUtc,
                s.CustomerName,
                s.Total,
                s.AmountPaid,
                s.AmountOnAccount,
                s.Status))
            .ToList();

        return new PagedResult<SaleListItem>(items, total, page, size);
    }

    private DateTimeOffset LocalDayStartUtc(DateOnly date)
    {
        var zone = _time.LocalTimeZone;
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A clock change at midnight can make local midnight invalid; use the
        // first valid instant of the day instead.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static SaleDetails ToDetails(Sale sale) =>
        new(
            sale.Id,
            sale.Number,
            sale.OccurredAtUtc,
            sale.CustomerId,
            sale.CustomerName,
            sale.Lines
                .OrderBy(l => l.ProductName, StringComparer.CurrentCultureIgnoreCase)
                .Select(l => new SaleLineItem(
                    l.ProductId,
                    l.Sku,
                    l.ProductName,
                    l.UnitPrice,
                    l.Quantity,
                    l.LineTotal))
                .ToList(),
            sale.Subtotal,
            sale.Discount,
            sale.Total,
            sale.AmountPaid,
            sale.AmountOnAccount,
            sale.PaymentMethod,
            sale.Notes,
            sale.Status,
            sale.VoidedAtUtc,
            sale.VoidReason);
}
