using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centurion.Core.Logging;

/// <summary>
/// 文件日志提供器：把全部日志级别（含 info）以单行格式写入程序根目录下的 logs 目录，
/// 按日期滚动（centurion-yyyyMMdd.log）。与 SimpleConsole 控制台输出共用同一日志管道，
/// 使每次运行的控制台内容都有完整的文件记录。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string? _currentFileName;

    /// <summary>
    /// 创建文件日志提供器。
    /// </summary>
    /// <param name="directory">日志目录；为 null 时使用程序根目录下的 logs。</param>
    public FileLoggerProvider(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_directory);
    }

    /// <summary>日志目录的绝对路径。</summary>
    public string DirectoryPath => _directory;

    /// <summary>创建指定类别的日志器实例。</summary>
    public ILogger CreateLogger(string categoryName) => new FileLogger(this);

    /// <summary>释放并关闭当前日志文件。</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _currentFileName = null;
        }
    }

    /// <summary>把一条日志写入当天的日志文件（按日期滚动，追加模式）。</summary>
    private void Write(LogLevel level, string message)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var fileName = $"centurion-{now:yyyyMMdd}.log";
            var fullPath = Path.Combine(_directory, fileName);

            if (!string.Equals(_currentFileName, fileName, StringComparison.Ordinal))
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _currentFileName = fileName;
            }

            // 以共享读写方式打开：多进程/残留进程同时追加时不会因文件锁抛异常
            _writer ??= new StreamWriter(
                new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            { AutoFlush = true };

            var levelText = level switch
            {
                LogLevel.Trace or LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => "none"
            };

            _writer.WriteLine($"{now:HH:mm:ss} {levelText}: {message}");
        }
    }

    /// <summary>单条文件日志写入器。</summary>
    private sealed class FileLogger(FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>始终启用；级别过滤由 LoggerFactory 的 provider 级规则控制（文件记录全部级别）。</summary>
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
