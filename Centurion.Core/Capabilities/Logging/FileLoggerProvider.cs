using System.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Logging;

/// <summary>
/// 文件日志提供器：把全部日志级别（含 info）以单行格式写入程序根目录下的 logs 目录，
/// 每次运行一个独立日志文件（centurion-yyyyMMdd-HHmmss.log），不再按日期滚动。
/// 创建时（即程序每次启动时）检查并清理超出保留天数的历史日志。
/// 与 SimpleConsole 控制台输出共用同一日志管道，使每次运行的控制台内容都有完整的文件记录。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private readonly string _fileName;

    /// <summary>
    /// 创建文件日志提供器；文件名按当前时间生成（每次运行一个文件），并立即清理过期日志。
    /// </summary>
    /// <param name="directory">日志目录；为 null 时使用程序根目录下的 logs。</param>
    /// <param name="retentionDays">保留天数；超过该天数的历史日志在启动时被清理，默认 7 天。</param>
    public FileLoggerProvider(string? directory = null, int retentionDays = 7)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_directory);
        _fileName = $"centurion-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        CleanupOldLogs(retentionDays);
    }

    /// <summary>日志目录的绝对路径。</summary>
    public string DirectoryPath => _directory;

    /// <summary>本次运行对应的日志文件名。</summary>
    public string CurrentLogFileName => _fileName;

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
        }
    }

    /// <summary>
    /// 启动清理：删除 logs 目录中最后写入时间早于保留窗口的 centurion-*.log。
    /// 清理失败只记录不影响启动（无日志器可用时静默跳过）。
    /// </summary>
    /// <param name="retentionDays">保留天数。</param>
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
            // 日志文件可能正被其他进程占用；清理失败不阻断程序启动
        }
        catch (UnauthorizedAccessException)
        {
            // 无删除权限时跳过清理
        }
    }

    /// <summary>把一条日志写入本次运行的日志文件（追加模式）。</summary>
    private void Write(LogLevel level, string message)
    {
        lock (_lock)
        {
            var fullPath = Path.Combine(_directory, _fileName);

            // 以共享读写方式打开：多进程/残留进程同时追加时不会因文件锁抛异常
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
