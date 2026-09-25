using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Centurion.Cli.Console;

/// <summary>
/// 与控制台日志文件保持一致的简单控制台格式化器：
/// 输出 `HH:mm:ss level: message`（无类别名），与 FileLoggerProvider 的文件行格式逐字一致；
/// 颜色仅按级别与语义渲染（[SUCCESS] 前缀绿、警告黄、错误/严重红，其余白色），
/// 重定向输出时自动无颜色。控制台显示的每一行与日志文件中的对应行完全相同。
/// </summary>
public sealed class PlainConsoleFormatter : ConsoleFormatter
{
    private const string SuccessPrefix = "[SUCCESS] ";

    /// <summary>创建使用 "plain" 名称注册的格式化器。</summary>
    public PlainConsoleFormatter() : base("plain")
    {
    }

    /// <summary>将一条日志格式化为单行控制台文本（与文件日志格式一致）。</summary>
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (logEntry.Exception is not null)
            message = string.IsNullOrEmpty(message)
                ? logEntry.Exception.ToString()
                : $"{message}{Environment.NewLine}{logEntry.Exception}";
        if (string.IsNullOrEmpty(message))
            return;

        var levelText = logEntry.LogLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };

        var color = logEntry.LogLevel switch
        {
            LogLevel.Warning => System.ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Critical => System.ConsoleColor.Red,
            _ when message.StartsWith(SuccessPrefix, StringComparison.Ordinal) => System.ConsoleColor.Green,
            _ => System.ConsoleColor.White
        };

        var line = $"{DateTime.Now:HH:mm:ss} {levelText}: {message}";
        var original = System.Console.ForegroundColor;
        try
        {
            System.Console.ForegroundColor = color;
            textWriter.WriteLine(line);
        }
        finally
        {
            System.Console.ForegroundColor = original;
        }
    }
}
