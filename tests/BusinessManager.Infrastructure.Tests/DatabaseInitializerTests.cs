using BusinessManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessManager.Infrastructure.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task NewDatabase_IsCreatedInWalMode_WithDailyBackup()
    {
        await using var db = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(db.Options.DatabasePath));
        Assert.Single(Directory.GetFiles(db.Options.BackupDirectory, "*.db"));

        await using var context = db.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UpgradingFromFirstRelease_KeepsProductsActiveWithStockRecords()
    {
        await using var db = new TestDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(db.Options.DatabasePath)!);

        await using (var context = db.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261114140705_InitialProducts");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Products (Id, Sku, Name, SellingPrice) VALUES ({0}, 'OLD-1', 'Legacy', '12.50')",
                Guid.NewGuid());
        }

        await db.Initializer.InitializeAsync();

        var product = Assert.Single((await db.Products.SearchAsync(new())).Items);
        Assert.True(product.IsActive);
        Assert.Equal(0, product.QuantityOnHand);

        // Stock can be received immediately after upgrade.
        await db.Products.ReceiveStockAsync(product.Id, 5, "Count");
        Assert.Equal(5, (await db.Products.GetAsync(product.Id)).QuantityOnHand);

        Assert.Contains(
            Directory.GetFiles(db.Options.BackupDirectory),
            f => f.Contains("premigration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseFromNewerVersion_IsRefused()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using (var context = db.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_Future', '99.0')");
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Initializer.InitializeAsync());
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public async Task CorruptFile_IsReportedClearly()
    {
        await using var db = new TestDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(db.Options.DatabasePath)!);
        await File.WriteAllTextAsync(db.Options.DatabasePath, new string('x', 4096));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Initializer.InitializeAsync());
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public async Task Backups_ArePrunedToConfiguredCount()
    {
        await using var db = await TestDatabase.CreateAsync();

        for (int i = 0; i < 5; i++)
        {
            await db.Backups.CreateBackupAsync("manual");
        }

        Assert.Equal(3, Directory.GetFiles(db.Options.BackupDirectory, "*.db").Length);

        // A backup is a working database.
        var latest = new DirectoryInfo(db.Options.BackupDirectory)
            .GetFiles("*.db").OrderByDescending(f => f.LastWriteTimeUtc).First();
        await using var connection = new SqliteConnection(
            DatabaseOptions.BuildConnectionString(latest.FullName, pooling: false));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Products";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
