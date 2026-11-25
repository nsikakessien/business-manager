namespace BusinessManager.Application.Reports;

public sealed record DailySales(DateOnly Date, int SaleCount, decimal Revenue);

public sealed record DashboardSummary(
    int SalesToday,
    decimal RevenueToday,
    decimal Revenue7Days,
    IReadOnlyList<DailySales> Last7Days,
    int ActiveProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    decimal TotalReceivables,
    int CustomersOwing);

public interface IDashboardService
{
    /// <summary>Summary figures; "today" uses the local time zone.</summary>
    Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}
