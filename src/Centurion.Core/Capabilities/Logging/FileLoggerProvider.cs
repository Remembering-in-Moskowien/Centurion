using System.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Logging;

/// <summary>
/// File logger provider: writes all log levels (including info) in a single-line format
/// to the logs directory under the app root, with one independent log file per run
/// (centurion-yyyyMMdd-HHmmss.log) and no daily rolling. On creation (i.e. every program
/// start) it checks and purges historical logs older than the retention window. It
/// shares the same logging pipeline as the SimpleConsole console output, so every run's
/// console content has a complete file record.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private readonly string _fileName;

    /// <summary>
    /// Creates the file logger provider; the file name is generated from the current time
    /// (one file per run), and expired logs are purged immediately.
    /// </summary>
    /// <param name="directory">Log directory; uses logs under the app root when null.</param>
    /// <param name="retentionDays">Retention days; historical logs older than this are purged at startup, default 7 days.</param>
    public FileLoggerProvider(string? directory = null, int retentionDays = 7)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_directory);
        _fileName = $"centurion-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        CleanupOldLogs(retentionDays);
    }

    /// <summary>Absolute path of the log directory.</summary>
    public string DirectoryPath => _directory;

    /// <summary>The log file name for this run.</summary>
    public string CurrentLogFileName => _fileName;

    /// <summary>Creates a logger instance for the given category.</summary>
    public ILogger CreateLogger(string categoryName) => new FileLogger(this);

    /// <summary>Releases and closes the current log file.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>
    /// Startup cleanup: deletes centurion-*.log files in the logs directory whose
    /// last-write time is older than the retention window. Cleanup failures are only
    /// logged and do not block startup (silently skipped when no logger is available).
    /// </summary>
    /// <param name="retentionDays">Retention days.</param>
    private void CleanupOldLogs(int retentionDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "centurion-*.log"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < cutoff)
                    File.Delete(file);
            }
        }
        catch (IOException)
        {
            // The log file may be held by another process; a cleanup failure does not block program startup
        }
        catch (UnauthorizedAccessException)
        {
            // Skip cleanup when there is no delete permission
        }
    }

    /// <summary>Writes one log entry to this run's log file (append mode).</summary>
    private void Write(LogLevel level, string message)
    {
        lock (_lock)
        {
            var fullPath = Path.Combine(_directory, _fileName);

            // Open in shared read/write mode: concurrent appends from multiple/leftover processes won't throw on file locks
            _writer ??= new StreamWriter(
                new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            { AutoFlush = true };

            var levelText = level switch
            {
                LogLevel.Trace or LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "crit",
                _ => "none"
            };

            _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {levelText}: {message}");
        }
    }

    /// <summary>Single-entry file logger.</summary>
    private sealed class FileLogger(FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Always enabled; level filtering is controlled by the LoggerFactory's provider-level rules (the file records all levels).</summary>
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
                message = string.IsNullOrEmpty(message)
                    ? exception.ToString()
                    : $"{message}{Environment.NewLine}{exception}";
            if (string.IsNullOrEmpty(message))
                return;

            owner.Write(logLevel, message);
        }
    }
}
