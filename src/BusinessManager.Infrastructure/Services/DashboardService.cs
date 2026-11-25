using BusinessManager.Application.Reports;
using BusinessManager.Domain.Sales;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Services;

public sealed class DashboardService : IDashboardService
{
    private const int TrendDays = 7;

    private readonly IDbContextFactory<BusinessManagerDbContext> _factory;
    private readonly TimeProvider _time;

    public DashboardService(
        IDbContextFactory<BusinessManagerDbContext> factory,
        TimeProvider? time = null)
    {
        _factory = factory;
        _time = time ?? TimeProvider.System;
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var zone = _time.LocalTimeZone;
        var nowLocal = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone);
        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var firstDay = today.AddDays(-(TrendDays - 1));

        // Pad the UTC window by a day each side; exact local-day bucketing
        // happens below, which keeps the SQL simple and handles DST correctly.
        var windowStart = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var recent = await db.Sales
            .AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.OccurredAtUtc >= windowStart)
            .Select(s => new { s.OccurredAtUtc, s.Total })
            .ToListAsync(ct);

        var byDay = recent
            .Select(s => new
            {
                Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.OccurredAtUtc, zone).DateTime),
                s.Total
            })
            .Where(s => s.Day >= firstDay && s.Day <= today)
            .GroupBy(s => s.Day)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Revenue: g.Sum(x => x.Total)));

        var trend = Enumerable.Range(0, TrendDays)
            .Select(i => firstDay.AddDays(i))
            .Select(day => byDay.TryGetValue(day, out var v)
                ? new DailySales(day, v.Count, v.Revenue)
                : new DailySales(day, 0, 0m))
            .ToList();

        var stock =
            from p in db.Products.AsNoTracking()
            where p.IsActive
            join s in db.Stocks.AsNoTracking() on p.Id equals s.ProductId into stocks
            from s in stocks.DefaultIfEmpty()
            select new { p.ReorderLevel, Quantity = s == null ? 0 : s.QuantityOnHand };

        int activeProducts = await stock.CountAsync(ct);
        int lowStock = await stock.CountAsync(x => x.ReorderLevel > 0 && x.Quantity <= x.ReorderLevel, ct);
        int outOfStock = await stock.CountAsync(x => x.Quantity == 0, ct);

        // Balances are summed in memory: SQLite stores decimals as text, and
        // only customers who owe money are fetched.
        var balances = await db.Customers
            .AsNoTracking()
            .Where(c => c.Balance > 0m)
            .Select(c => c.Balance)
            .ToListAsync(ct);

        var todays = trend[^1];

        return new DashboardSummary(
            todays.SaleCount,
            todays.Revenue,
            trend.Sum(d => d.Revenue),
            trend,
            activeProducts,
            lowStock,
            outOfStock,
            balances.Sum(),
            balances.Count);
    }
}
