using BusinessManager.Domain.Customers;
using BusinessManager.Domain.Inventory;
using BusinessManager.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BusinessManager.Infrastructure.Persistence;

public sealed class BusinessManagerDbContext : DbContext
{
    /// <summary>Shadow optimistic-concurrency token on mutable aggregates.</summary>
    internal const string VersionProperty = "Version";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryStock> Stocks => Set<InventoryStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerTransaction> CustomerTransactions => Set<CustomerTransaction>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();

    public BusinessManagerDbContext(
        DbContextOptions<BusinessManagerDbContext> options)
        : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native date type. Storing DateTimeOffset as a 64-bit
        // integer lets the database sort and range-filter dates efficiently.
        // All stored values are UTC, so ordering is chronological.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProducts(modelBuilder);
        ConfigureStock(modelBuilder);
        ConfigureCustomers(modelBuilder);
        ConfigureSales(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void BumpVersions()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified ||
                entry.Metadata.FindProperty(VersionProperty) is null)
            {
                continue;
            }

            var version = entry.Property(VersionProperty);
            version.CurrentValue = (long)(version.OriginalValue ?? 0L) + 1;
        }
    }

    private static void ConfigureProducts(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();

        product.ToTable("Products");
        product.HasKey(p => p.Id);
        product.Property(p => p.Id).ValueGeneratedNever();

        product.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(Product.MaxSkuLength);

        product.HasIndex(p => p.Sku).IsUnique();

        product.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Product.MaxNameLength);

        product.HasIndex(p => p.Name);

        product.Property(p => p.SellingPrice).IsRequired();
        // Note: the migration that added IsActive defaults existing rows to
        // active; the model has no default so EF always writes the value.
        product.HasIndex(p => p.IsActive);
    }

    private static void ConfigureStock(ModelBuilder modelBuilder)
    {
        var stock = modelBuilder.Entity<InventoryStock>();

        stock.ToTable("InventoryStocks", t =>
            t.HasCheckConstraint("CK_InventoryStocks_Quantity", "\"QuantityOnHand\" >= 0"));

        stock.HasKey(s => s.ProductId);
        stock.Property(s => s.ProductId).ValueGeneratedNever();

        stock.HasOne<Product>()
            .WithOne()
            .HasForeignKey<InventoryStock>(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        stock.Property<long>(VersionProperty).IsConcurrencyToken().HasDefaultValue(0L);

        stock.HasMany(s => s.Movements)
            .WithOne()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        stock.Navigation(s => s.Movements)
            .HasField("_movements")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        var movement = modelBuilder.Entity<StockMovement>();

        movement.ToTable("StockMovements");
        movement.HasKey(m => m.Id);
        movement.Property(m => m.Id).ValueGeneratedNever();
        movement.Property(m => m.Reason).IsRequired().HasMaxLength(StockMovement.MaxReasonLength);
        movement.HasIndex(m => new { m.ProductId, m.OccurredAtUtc });
        movement.HasIndex(m => m.ReferenceId);
    }

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<Customer>();

        customer.ToTable("Customers");
        customer.HasKey(c => c.Id);
        customer.Property(c => c.Id).ValueGeneratedNever();
        customer.Property(c => c.Name).IsRequired().HasMaxLength(Customer.MaxNameLength);
        customer.Property(c => c.Phone).HasMaxLength(Customer.MaxPhoneLength);
        customer.Property(c => c.Email).HasMaxLength(Customer.MaxEmailLength);
        customer.Property(c => c.Notes).HasMaxLength(Customer.MaxNotesLength);
        customer.Property(c => c.CreditLimit).IsRequired();
        customer.Property(c => c.Balance).IsRequired();
        customer.Ignore(c => c.AvailableCredit);
        customer.HasIndex(c => c.Name);
        customer.HasIndex(c => c.Phone);

        customer.Property<long>(VersionProperty).IsConcurrencyToken().HasDefaultValue(0L);

        customer.HasMany(c => c.Transactions)
            .WithOne()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        customer.Navigation(c => c.Transactions)
            .HasField("_transactions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        var transaction = modelBuilder.Entity<CustomerTransaction>();

        transaction.ToTable("CustomerTransactions");
        transaction.HasKey(t => t.Id);
        transaction.Property(t => t.Id).ValueGeneratedNever();
        transaction.Property(t => t.Description)
            .IsRequired()
            .HasMaxLength(CustomerTransaction.MaxDescriptionLength);
        transaction.HasIndex(t => new { t.CustomerId, t.OccurredAtUtc });
        transaction.HasIndex(t => t.ReferenceId);
    }

    private static void ConfigureSales(ModelBuilder modelBuilder)
    {
        var sale = modelBuilder.Entity<Sale>();

        sale.ToTable("Sales");
        sale.HasKey(s => s.Id);
        sale.Property(s => s.Id).ValueGeneratedNever();
        sale.Ignore(s => s.Number);
        sale.HasIndex(s => s.Sequence).IsUnique();
        sale.HasIndex(s => s.OccurredAtUtc);
        sale.HasIndex(s => s.CustomerId);
        sale.Property(s => s.CustomerName).HasMaxLength(Customer.MaxNameLength);
        sale.Property(s => s.Notes).HasMaxLength(Sale.MaxNotesLength);
        sale.Property(s => s.VoidReason).HasMaxLength(Sale.MaxVoidReasonLength);

        sale.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        sale.Property<long>(VersionProperty).IsConcurrencyToken().HasDefaultValue(0L);

        sale.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey(l => l.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        sale.Navigation(s => s.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        var line = modelBuilder.Entity<SaleLine>();

        line.ToTable("SaleLines");
        line.HasKey(l => l.Id);
        line.Property(l => l.Id).ValueGeneratedNever();
        line.Property(l => l.Sku).IsRequired().HasMaxLength(Product.MaxSkuLength);
        line.Property(l => l.ProductName).IsRequired().HasMaxLength(Product.MaxNameLength);
        line.HasIndex(l => l.ProductId);

        line.HasOne<Product>()
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
