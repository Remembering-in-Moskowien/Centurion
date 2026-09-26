using Centurion.Abstractions;
using Centurion.Abstractions.Utils;
using Centurion.Models.Console;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Centurion.Cli;

/// <summary>
/// 统一错误输出：日志通道输出红色失败行（与文件日志一致），
/// 并附加可操作的修复建议（黄色提示行），帮助新用户自行解决问题。
/// </summary>
public static class CliErrorPrinter
{
    /// <summary>
    /// 记录失败日志并输出修复建议。
    /// </summary>
    /// <param name="logger">命令日志器。</param>
    /// <param name="ex">捕获的异常。</param>
    /// <param name="context">失败上下文描述（写入日志）。</param>
    public static void Print(ILogger logger, Exception ex, string context)
    {
        FailLogGate.Log(logger, ex, context);
        var hint = Suggest(ex);
        if (hint is not null)
            ConsoleServices.Output.WriteWarning(ConsoleServices.T("Suggestion: {0}", hint));
    }

    /// <summary>把常见异常映射为可操作的修复建议；无法识别时给通用指引。</summary>
    public static string? Suggest(Exception ex) => ex switch
    {
        Centurion.Core.Capabilities.Managers.Media.ModelMissingException m =>
            ConsoleServices.T("Model '{0}' is not installed. Run: Centurion models install {0}", m.ModelName),
        FileNotFoundException => ConsoleServices.T("Input file not found. Check the path (or use the init wizard)"),
        DirectoryNotFoundException => ConsoleServices.T("Output directory not found. Create it or change the -o path"),
        JsonException => ConsoleServices.T("File is not valid JSON (or schema mismatch). Run: Centurion validate <file>"),
        UnauthorizedAccessException => ConsoleServices.T("No write permission. Check the output directory or use another path"),
        HttpRequestException => ConsoleServices.T("Network request failed. Check connectivity, or adjust --github-proxy / direct"),
        TaskCanceledException => ConsoleServices.T("Operation cancelled (timeout or user interrupt)"),
        OperationCanceledException => ConsoleServices.T("Operation cancelled"),
        _ => ConsoleServices.T("Add --verbose for detailed logs; if it recurs, attach the logs directory when reporting")
    };
}
