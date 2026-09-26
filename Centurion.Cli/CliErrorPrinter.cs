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
            ConsoleServices.Output.WriteWarning($"建议: {hint}");
    }

    /// <summary>把常见异常映射为可操作的修复建议；无法识别时给通用指引。</summary>
    public static string? Suggest(Exception ex) => ex switch
    {
        Centurion.Core.Capabilities.Managers.Media.ModelMissingException m =>
            $"模型 '{m.ModelName}' 未安装。运行: Centurion models install {m.ModelName}",
        FileNotFoundException => "输入文件不存在。请检查路径（可用 init 向导生成配置）",
        DirectoryNotFoundException => "输出目录不存在。请先创建目录或修改 -o 路径",
        JsonException => "文件不是有效的 JSON（或 Schema 不匹配）。运行: Centurion validate <file>",
        UnauthorizedAccessException => "无写入权限。请检查输出目录权限或改用其它路径",
        HttpRequestException => "网络请求失败。请检查网络连接，或调整 --github-proxy / 直连",
        TaskCanceledException => "操作已取消（超时或用户中断）",
        OperationCanceledException => "操作已取消",
        _ => "加 --verbose 查看详细日志；若反复出现，请附带 logs 目录内容反馈问题"
    };
}
