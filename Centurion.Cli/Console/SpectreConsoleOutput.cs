using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// 基于 Spectre.Console 的控制台输出实现。
/// <para>
/// 每类输出都同时写入控制台（渲染层）与 ILogger（日志层，进而写入 logs 文件）：
/// 信息类输出直接渲染为白色/绿色行，并转发 info 日志（控制台侧按类别过滤，
/// 避免与格式化器重复显示）；警告与错误仅经日志通道输出，由
/// <see cref="PlainConsoleFormatter"/> 统一渲染为黄/红行，保证与文件日志逐字一致。
/// </para>
/// </summary>
public class SpectreConsoleOutput(ILogger<SpectreConsoleOutput> logger) : IConsoleOutput
{
    private readonly ILogger<SpectreConsoleOutput> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>生成与日志文件一致的 info 前缀（HH:mm:ss info:）。</summary>
    private static string InfoPrefix => $"{DateTime.Now:HH:mm:ss} [blue]info[/]: ";

    private static string SuccessPrefix => $"{DateTime.Now:HH:mm:ss} [green]succ[/]: ";

    /// <summary>
    /// 以白色向控制台写入文本（不换行），并记录 info 日志。
    /// 不换行的拼接语义不适合加前缀，保持原样（当前无调用点）。
    /// </summary>
    /// <param name="message">要写入的文本。</param>
    public void Write(string message)
    {
        AnsiConsole.Markup($"{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// 以白色向控制台写入一行文本（带日志式前缀），并记录 info 日志。
    /// </summary>
    /// <param name="message">要写入的文本。</param>
    public void WriteLine(string message)
    {
        AnsiConsole.MarkupLine($"{InfoPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// 输出一条错误信息（红色，经日志通道渲染，与文件日志逐字一致）。
    /// </summary>
    /// <param name="message">错误信息文本。</param>
    public void WriteError(string message) => _logger.LogError(message);

    /// <summary>
    /// 输出一条警告信息（黄色，经日志通道渲染，与文件日志逐字一致）。
    /// </summary>
    /// <param name="message">警告信息文本。</param>
    public void WriteWarning(string message) => _logger.LogWarning(message);

    /// <summary>
    /// 以绿色向控制台写入一条成功信息（带日志式前缀与 [SUCCESS] 标记），
    /// 并记录带 [SUCCESS] 前缀的 info 日志，控制台与文件逐字一致。
    /// </summary>
    /// <param name="message">成功信息文本。</param>
    public void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"{SuccessPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation($"{message}");
    }

    /// <summary>
    /// 以白色向控制台写入一行提示信息（带日志式前缀），并记录 info 日志。
    /// </summary>
    /// <param name="message">提示信息文本。</param>
    public void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"{InfoPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// 向控制台写入 Spectre 标记文本（不换行），并记录剥离标记后的 info 日志。
    /// </summary>
    /// <param name="markup">Spectre 标记字符串。</param>
    public void WriteMarkup(string markup)
    {
        AnsiConsole.Write(markup);
        _logger.LogInformation(StripMarkup(markup));
    }

    /// <summary>
    /// 向控制台写入一行 Spectre 标记文本（带日志式前缀），并记录剥离标记后的 info 日志。
    /// </summary>
    /// <param name="markup">Spectre 标记字符串。</param>
    public void WriteMarkupLine(string markup)
    {
        AnsiConsole.MarkupLine($"{InfoPrefix}{markup}");
        _logger.LogInformation(StripMarkup(markup));
    }

    /// <summary>剥离 Spectre 标记标签（如 [green]、[/]），保留纯文本用于日志记录。</summary>
    private static string StripMarkup(string markup) =>
        Regex.Replace(markup, @"\[[^\]]*\]", string.Empty);
}
