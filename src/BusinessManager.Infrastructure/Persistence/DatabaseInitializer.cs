using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessManager.Infrastructure.Persistence;

/// <summary>
/// Prepares the database at startup: creates it, takes a safety backup,
/// applies migrations and checks integrity.
/// </summary>
public sealed class DatabaseInitializer
{
    private static readonly TimeSpan DailyBackupInterval = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<BusinessManagerDbContext> _factory;
    private readonly DatabaseOptions _options;
    private readonly DatabaseBackupService _backups;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<BusinessManagerDbContext> factory,
        DatabaseOptions options,
        DatabaseBackupService backups,
        ILogger<DatabaseInitializer>? logger = null)
    {
        _factory = factory;
        _options = options;
        _backups = backups;
        _logger = logger ?? NullLogger<DatabaseInitializer>.Instance;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath)!);

        bool databaseExists = File.Exists(_options.DatabasePath) &&
                              new FileInfo(_options.DatabasePath).Length > 0;

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (databaseExists)
        {
            await EnsureIntegrityAsync(db, ct);

            var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();

            if (applied.Any(m => !known.Contains(m)))
            {
                // Running an older build against a newer database could
                // silently corrupt data, so refuse instead.
                throw new InvalidOperationException(
                    "The database was created by a newer version of Business Manager. " +
                    "Please install the latest version.");
            }

            if (known.Count > applied.Count)
            {
                await _backups.CreateBackupAsync("premigration", ct);
            }
        }

        _logger.LogInformation("Applying database migrations to {Path}", _options.DatabasePath);
        await db.Database.MigrateAsync(ct);

        // Write-ahead logging: readers don't block the writer and a crash
        // mid-write cannot corrupt committed data. The setting is persistent.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        await TakeDailyBackupAsync(ct);
    }

    private async Task EnsureIntegrityAsync(BusinessManagerDbContext db, CancellationToken ct)
    {
        try
        {
            var result = new List<string>();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(reader.GetString(0));
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }

            if (result.Count != 1 || !string.Equals(result[0], "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The database file is damaged. Restore the most recent backup from " +
                    $"'{_options.BackupDirectory}'. Details: {string.Join("; ", result.Take(5))}");
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 26 /* NOTADB */ or 11 /* CORRUPT */)
        {
            throw new InvalidOperationException(
                "The database file is damaged or is not a Business Manager database. " +
                $"Restore the most recent backup from '{_options.BackupDirectory}'.",
                ex);
        }
    }

    private async Task TakeDailyBackupAsync(CancellationToken ct)
    {
        try
        {
            var last = _backups.GetLastBackupTimeUtc();
            if (last is null || DateTime.UtcNow - last.Value >= DailyBackupInterval)
            {
                await _backups.CreateBackupAsync("daily", ct);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            // A failed routine backup must not stop the business from trading.
            _logger.LogError(ex, "Daily backup failed");
        }
    }
}
