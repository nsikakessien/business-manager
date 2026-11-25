using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.Logging;

/// <summary>
/// Minimal rolling file logger: one file per day, old files removed.
/// Writes are serialised and never throw into the application.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly int _daysToKeep;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string directory, int daysToKeep = 30)
    {
        _directory = directory;
        _daysToKeep = daysToKeep;

        try
        {
            Directory.CreateDirectory(_directory);
            PruneOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop the application.
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var builder = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(category).Append(": ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        string path = Path.Combine(
            _directory,
            $"business-manager-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

        lock (_lock)
        {
            try
            {
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void PruneOldFiles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_daysToKeep);
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("business-manager-*.log"))
        {
            if (file.LastWriteTimeUtc < cutoff)
            {
                file.Delete();
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(_category, logLevel, formatter(state, exception), exception);
        }
    }
}
