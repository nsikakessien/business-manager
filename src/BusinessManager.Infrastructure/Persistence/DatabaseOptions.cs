using Microsoft.Data.Sqlite;

namespace BusinessManager.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public DatabaseOptions(string databasePath, string? backupDirectory = null, int backupsToKeep = 14)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        BackupDirectory = Path.GetFullPath(
            backupDirectory ?? Path.Combine(Path.GetDirectoryName(DatabasePath)!, "Backups"));
        BackupsToKeep = Math.Max(1, backupsToKeep);
    }

    public string DatabasePath { get; }
    public string BackupDirectory { get; }
    public int BackupsToKeep { get; }

    public string ConnectionString => BuildConnectionString(DatabasePath);

    public static string BuildConnectionString(string path, bool pooling = true) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = pooling,
            // Seconds to wait for a lock held by another connection before failing.
            DefaultTimeout = 30
        }.ToString();
}
