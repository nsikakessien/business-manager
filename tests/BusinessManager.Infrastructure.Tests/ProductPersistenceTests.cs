using BusinessManager.Domain.Inventory;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessManager.Infrastructure.Tests;

public sealed class ProductPersistenceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"business-manager-tests-{Guid.NewGuid():N}.db");

    private BusinessManagerDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();

        var options =
            new DbContextOptionsBuilder<BusinessManagerDbContext>()
                .UseSqlite(connectionString)
                .Options;

        return new BusinessManagerDbContext(options);
    }

    [Fact]
    public async Task SavedProduct_CanBeLoadedAfterDatabaseIsReopened()
    {
        var product = new Product(
            "  water-001  ",
            "  Bottled Water  ",
            4000.50m);

        await using (var writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();

            writeContext.Products.Add(product);

            await writeContext.SaveChangesAsync();
        }

        // The previous context and its connection have been disposed.
        await using (var readContext = CreateContext())
        {
            var loaded = await readContext.Products
                .AsNoTracking()
                .SingleAsync(p => p.Id == product.Id);

            Assert.Equal(product.Id, loaded.Id);
            Assert.Equal("WATER-001", loaded.Sku);
            Assert.Equal("Bottled Water", loaded.Name);
            Assert.Equal(4000.50m, loaded.SellingPrice);
        }
    }

    [Fact]
    public async Task DuplicateNormalizedSku_IsRejectedWithoutAddingProduct()
    {
        await using (var setupContext = CreateContext())
        {
            await setupContext.Database.MigrateAsync();

            setupContext.Products.Add(
                new Product("WATER-001", "Bottled Water", 4000m));

            await setupContext.SaveChangesAsync();
        }

        await using (var duplicateContext = CreateContext())
        {
            duplicateContext.Products.Add(
                new Product("  water-001  ", "Another Product", 5000m));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                async () =>
                {
                    await duplicateContext.SaveChangesAsync();
                });

            var sqliteException =
                Assert.IsType<SqliteException>(exception.InnerException);

            // SQLite's extended error code for a unique constraint violation.
            Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
        }

        await using (var verificationContext = CreateContext())
        {
            var remaining = await verificationContext.Products
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("Bottled Water", remaining.Name);
            Assert.Equal(4000m, remaining.SellingPrice);
        }
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = _databasePath + suffix;

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}