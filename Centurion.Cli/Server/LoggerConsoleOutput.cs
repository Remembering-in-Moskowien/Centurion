using Centurion.Core.Capabilities.Infrastructure;using Microsoft.Extensions.Logging;
using Centurion.Models.Console;
namespace Centurion.Cli.Server;

/// <summary>
/// 控制台输出适配器：把命令内部经 <see cref="ConsoleServices"/> 输出的消息
/// 转发到 Server 的 ILogger（信息级），使命令执行过程可被服务器日志与日志文件捕获。
/// </summary>
public sealed class LoggerConsoleOutput(ILogger logger) : IConsoleOutput
{
    /// <summary>写入一段文本，不换行（信息级）。</summary>
    /// <param name="message">文本内容。</param>
    public void Write(string message) => logger.LogInformation("{Message}", message);

    /// <summary>写入一行文本并换行（信息级）。</summary>
    /// <param name="message">文本内容。</param>
    public void WriteLine(string message) => logger.LogInformation("{Message}", message);

    /// <summary>以错误样式输出一行文本（错误级）。</summary>
    /// <param name="message">错误文本。</param>
    public void WriteError(string message) => logger.LogError("{Message}", message);

    /// <summary>以警告样式输出一行文本（警告级）。</summary>
    /// <param name="message">警告文本。</param>
    public void WriteWarning(string message) => logger.LogWarning("{Message}", message);

    /// <summary>以成功样式输出一行文本（信息级）。</summary>
    /// <param name="message">成功文本。</param>
    public void WriteSuccess(string message) => logger.LogInformation("{Message}", message);

    /// <summary>以普通信息样式输出一行文本（信息级）。</summary>
    /// <param name="message">信息文本。</param>
    public void WriteInfo(string message) => logger.LogInformation("{Message}", message);

    /// <summary>写入富文本标记（信息级，原样保留标记）。</summary>
    /// <param name="markup">富文本内容。</param>
    public void WriteMarkup(string markup) => logger.LogInformation("{Markup}", markup);

    /// <summary>写入富文本标记并换行（信息级，原样保留标记）。</summary>
    /// <param name="markup">富文本内容。</param>
    public void WriteMarkupLine(string markup) => logger.LogInformation("{Markup}", markup);
}
