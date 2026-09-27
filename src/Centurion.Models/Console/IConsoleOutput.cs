namespace Centurion.Models.Console;

/// <summary>控制台文本输出端口，抽象对命令行的各类写入操作。</summary>
public interface IConsoleOutput
{
    /// <summary>写入一段文本，不换行。</summary>
    /// <param name="message">要写入的文本。</param>
    void Write(string message);
    /// <summary>写入一行文本并换行。</summary>
    /// <param name="message">要写入的文本。</param>
    void WriteLine(string message);
    /// <summary>以错误样式（通常为红色）输出一行文本。</summary>
    /// <param name="message">要输出的错误文本。</param>
    void WriteError(string message);
    /// <summary>以警告样式（通常为黄色）输出一行文本。</summary>
    /// <param name="message">要输出的警告文本。</param>
    void WriteWarning(string message);
    /// <summary>以成功样式（通常为绿色）输出一行文本。</summary>
    /// <param name="message">要输出的成功文本。</param>
    void WriteSuccess(string message);
    /// <summary>以普通信息样式输出一行文本。</summary>
    /// <param name="message">要输出的信息文本。</param>
    void WriteInfo(string message);
    /// <summary>写入支持颜色/样式标记的富文本，不换行。</summary>
    /// <param name="markup">含样式标记的文本。</param>
    void WriteMarkup(string markup); // 支持颜色标记
    /// <summary>写入支持颜色/样式标记的富文本，并换行。</summary>
    /// <param name="markup">含样式标记的文本。</param>
    void WriteMarkupLine(string markup);
}