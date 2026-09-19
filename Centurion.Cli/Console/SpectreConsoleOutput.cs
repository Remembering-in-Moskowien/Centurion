using Centurion.Models.Console;
using Spectre.Console;

namespace Centurion.Cli.Console;

/// <summary>
/// 基于 Spectre.Console 的控制台输出实现，提供普通文本与彩色分级日志输出。
/// </summary>
public class SpectreConsoleOutput : IConsoleOutput
{
    /// <summary>
    /// 向控制台写入文本（不换行）。
    /// </summary>
    /// <param name="message">要写入的文本。</param>
    public void Write(string message)
    {
        AnsiConsole.Write(message);
    }

    /// <summary>
    /// 向控制台写入一行文本。
    /// </summary>
    /// <param name="message">要写入的文本。</param>
    public void WriteLine(string message)
    {
        AnsiConsole.WriteLine(message);
    }

    /// <summary>
    /// 以红色高亮写入一条错误信息。
    /// </summary>
    /// <param name="message">错误信息文本。</param>
    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{message.EscapeMarkup()}[/]");
    }

    /// <summary>
    /// 以黄色高亮写入一条警告信息。
    /// </summary>
    /// <param name="message">警告信息文本。</param>
    public void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{message.EscapeMarkup()}[/]");
    }

    /// <summary>
    /// 以绿色高亮写入一条成功信息。
    /// </summary>
    /// <param name="message">成功信息文本。</param>
    public void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{message.EscapeMarkup()}[/]");
    }

    /// <summary>
    /// 以蓝色高亮写入一条提示信息。
    /// </summary>
    /// <param name="message">提示信息文本。</param>
    public void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]{message.EscapeMarkup()}[/]");
    }

    /// <summary>
    /// 直接写入 Spectre 标记文本（不换行）。
    /// </summary>
    /// <param name="markup">Spectre 标记字符串。</param>
    public void WriteMarkup(string markup)
    {
        AnsiConsole.Write(markup);
    }

    /// <summary>
    /// 直接写入一行 Spectre 标记文本。
    /// </summary>
    /// <param name="markup">Spectre 标记字符串。</param>
    public void WriteMarkupLine(string markup)
    {
        AnsiConsole.MarkupLine(markup);
    }
}