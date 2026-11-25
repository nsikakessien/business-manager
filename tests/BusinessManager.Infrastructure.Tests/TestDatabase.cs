using BusinessManager.Application.Customers;
using BusinessManager.Application.Products;
using BusinessManager.Application.Sales;
using BusinessManager.Infrastructure.Persistence;
using BusinessManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Tests;

/// <summary>
/// A throwaway on-disk database with the real services wired up.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"business-manager-tests-{Guid.NewGuid():N}");

    public TestDatabase()
    {
        Directory.CreateDirectory(_directory);
        Options = new DatabaseOptions(Path.Combine(_directory, "test.db"), backupsToKeep: 3);
        Factory = new ContextFactory(DatabaseOptions.BuildConnectionString(Options.DatabasePath, pooling: false));
        Backups = new DatabaseBackupService(Options);
        Initializer = new DatabaseInitializer(Factory, Options, Backups);
        Products = new ProductService(Factory);
        Customers = new CustomerService(Factory);
        Sales = new SalesService(Factory);
        Dashboard = new DashboardService(Factory);
    }

    public DatabaseOptions Options { get; }
    public IDbContextFactory<BusinessManagerDbContext> Factory { get; }
    public DatabaseBackupService Backups { get; }
    public DatabaseInitializer Initializer { get; }
    public ProductService Products { get; }
    public CustomerService Customers { get; }
    public SalesService Sales { get; }
    public DashboardService Dashboard { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var db = new TestDatabase();
        await db.Initializer.InitializeAsync();
        return db;
    }

    public BusinessManagerDbContext CreateContext() => Factory.CreateDbContext();

    public Task<Guid> AddProductAsync(string sku, decimal price, int stock = 0, int reorderLevel = 0, string? name = null) =>
        Products.CreateAsync(new CreateProductRequest(sku, name ?? $"Product {sku}", price, reorderLevel, stock));

    public Task<Guid> AddCustomerAsync(string name = "Ada Okafor", decimal creditLimit = 0m) =>
        Customers.CreateAsync(new CustomerDetailsRequest(name, "0803 123 4567", null, null, creditLimit));

    public Task<SaleDetails> SellAsync(
        Guid productId,
        int quantity,
        decimal amountPaid,
        Guid? customerId = null,
        decimal discount = 0m) =>
        Sales.CreateAsync(new CreateSaleRequest(
            new[] { new SaleLineRequest(productId, quantity) },
            customerId,
            discount,
            amountPaid,
            amountPaid > 0 ? Domain.Common.PaymentMethod.Cash : null,
            null));

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS cleans the temp folder eventually.
        }

        return ValueTask.CompletedTask;
    }

    private sealed class ContextFactory : IDbContextFactory<BusinessManagerDbContext>
    {
        private readonly DbContextOptions<BusinessManagerDbContext> _options;

        public ContextFactory(string connectionString)
        {
            _options = new DbContextOptionsBuilder<BusinessManagerDbContext>()
                .UseSqlite(connectionString)
                .Options;
        }

        public BusinessManagerDbContext CreateDbContext() => new(_options);
    }
}
