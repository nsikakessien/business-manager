using System.Linq.Expressions;
using BusinessManager.Application.Common;
using BusinessManager.Application.Customers;
using BusinessManager.Domain.Common;
using BusinessManager.Domain.Customers;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Services;

public sealed class CustomerService : ICustomerService
{
    private static readonly Expression<Func<Customer, CustomerListItem>> ToItem =
        c => new CustomerListItem(
            c.Id,
            c.Name,
            c.Phone,
            c.Email,
            c.Notes,
            c.CreditLimit,
            c.Balance,
            c.IsActive);

    private readonly IDbContextFactory<BusinessManagerDbContext> _factory;

    public CustomerService(IDbContextFactory<BusinessManagerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<PagedResult<CustomerListItem>> SearchAsync(
        CustomerQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, size, skip) = Paging.Normalize(query.Page, query.PageSize);

        await using var db = await _factory.CreateDbContextAsync(ct);

        IQueryable<Customer> customers = db.Customers.AsNoTracking();

        if (!query.IncludeArchived)
        {
            customers = customers.Where(c => c.IsActive);
        }

        if (LikePattern.Contains(query.SearchText) is { } pattern)
        {
            customers = customers.Where(c =>
                EF.Functions.Like(c.Name, pattern, LikePattern.Escape) ||
                (c.Phone != null && EF.Functions.Like(c.Phone, pattern, LikePattern.Escape)) ||
                (c.Email != null && EF.Functions.Like(c.Email, pattern, LikePattern.Escape)));
        }

        if (query.WithBalanceOnly)
        {
            customers = customers.Where(c => c.Balance > 0m);
        }

        int total = await customers.CountAsync(ct);

        var items = await customers
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip(skip)
            .Take(size)
            .Select(ToItem)
            .ToListAsync(ct);

        return new PagedResult<CustomerListItem>(items, total, page, size);
    }

    public async Task<CustomerListItem> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.Customers
                   .AsNoTracking()
                   .Where(c => c.Id == id)
                   .Select(ToItem)
                   .SingleOrDefaultAsync(ct)
               ?? throw new NotFoundException("The customer");
    }

    public async Task<Guid> CreateAsync(CustomerDetailsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = ErrorTranslation.Domain(() => new Customer(
            request.Name,
            request.Phone,
            request.Email,
            request.Notes,
            request.CreditLimit));

        await using var db = await _factory.CreateDbContextAsync(ct);

        db.Customers.Add(customer);
        await ErrorTranslation.SaveAsync(db, ct);

        return customer.Id;
    }

    public async Task UpdateAsync(Guid id, CustomerDetailsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var customer = await FindAsync(db, id, ct);

        ErrorTranslation.Domain(() => customer.UpdateDetails(
            request.Name,
            request.Phone,
            request.Email,
            request.Notes,
            request.CreditLimit));

        await ErrorTranslation.SaveAsync(db, ct);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var customer = await FindAsync(db, id, ct);

        if (isActive)
        {
            customer.Restore();
        }
        else
        {
            ErrorTranslation.Domain(customer.Archive);
        }

        await ErrorTranslation.SaveAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecordPaymentAsync(
        Guid customerId,
        decimal amount,
        PaymentMethod method,
        string? note,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var customer = await FindAsync(db, customerId, ct);

        ErrorTranslation.Domain(() => customer.ReceivePayment(amount, method, note));

        await ErrorTranslation.SaveAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PagedResult<CustomerTransactionItem>> GetTransactionsAsync(
        Guid customerId,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var (number, size, skip) = Paging.Normalize(page, pageSize);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var query = db.CustomerTransactions.AsNoTracking().Where(t => t.CustomerId == customerId);

        int total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.OccurredAtUtc)
            .Skip(skip)
            .Take(size)
            .Select(t => new CustomerTransactionItem(
                t.Id,
                t.Type,
                t.Amount,
                t.BalanceAfter,
                t.PaymentMethod,
                t.Description,
                t.ReferenceId,
                t.OccurredAtUtc))
            .ToListAsync(ct);

        return new PagedResult<CustomerTransactionItem>(items, total, number, size);
    }

    private static async Task<Customer> FindAsync(
        BusinessManagerDbContext db,
        Guid id,
        CancellationToken ct) =>
        await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("The customer");
}
