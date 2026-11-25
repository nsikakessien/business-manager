using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessManager.Infrastructure.Persistence;

/// <summary>
/// Creates consistent online copies of the database using SQLite's backup API,
/// which is safe while the application is using the database.
/// </summary>
public sealed class DatabaseBackupService
{
    private const string FilePrefix = "business-manager-";
    private const string FileExtension = ".db";

    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseBackupService(
        DatabaseOptions options,
        ILogger<DatabaseBackupService>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<DatabaseBackupService>.Instance;
    }

    public string BackupDirectory => _options.BackupDirectory;

    public async Task<string> CreateBackupAsync(string label, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_options.BackupDirectory);

            string safeLabel = new(label.Where(char.IsLetterOrDigit).Take(30).ToArray());
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string finalPath = Path.Combine(
                _options.BackupDirectory,
                $"{FilePrefix}{stamp}-{(safeLabel.Length == 0 ? "backup" : safeLabel)}{FileExtension}");
            string tempPath = finalPath + ".tmp";

            await Task.Run(() =>
            {
                using (var source = new SqliteConnection(
                    DatabaseOptions.BuildConnectionString(_options.DatabasePath, pooling: false)))
                using (var destination = new SqliteConnection(
                    DatabaseOptions.BuildConnectionString(tempPath, pooling: false)))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination);
                }

                // Only a complete copy gets the final name.
                File.Move(tempPath, finalPath, overwrite: true);
            }, ct);

            _logger.LogInformation("Database backup created at {Path}", finalPath);
            PruneOldBackups();
            return finalPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    public DateTime? GetLastBackupTimeUtc()
    {
        if (!Directory.Exists(_options.BackupDirectory))
        {
            return null;
        }

        return GetBackupFiles().Select(f => (DateTime?)f.LastWriteTimeUtc).FirstOrDefault();
    }

    private IEnumerable<FileInfo> GetBackupFiles() =>
        new DirectoryInfo(_options.BackupDirectory)
            .EnumerateFiles($"{FilePrefix}*{FileExtension}")
            .OrderByDescending(f => f.LastWriteTimeUtc);

    private void PruneOldBackups()
    {
        foreach (var file in GetBackupFiles().Skip(_options.BackupsToKeep))
        {
            try
            {
                file.Delete();
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete old backup {Path}", file.FullName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not delete old backup {Path}", file.FullName);
            }
        }

        // Leftovers from an interrupted backup.
        foreach (var tmp in Directory.EnumerateFiles(_options.BackupDirectory, "*.tmp"))
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
