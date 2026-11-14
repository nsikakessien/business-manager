using BusinessManager.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Persistence;

public sealed class BusinessManagerDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    public BusinessManagerDbContext(
        DbContextOptions<BusinessManagerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var product = modelBuilder.Entity<Product>();

        product.ToTable("Products");

        product.HasKey(p => p.Id);

        product.Property(p => p.Id)
            .ValueGeneratedNever();

        product.Property(p => p.Sku)
            .IsRequired();

        product.HasIndex(p => p.Sku)
            .IsUnique();

        product.Property(p => p.Name)
            .IsRequired();

        product.Property(p => p.SellingPrice)
            .IsRequired();
    }
}