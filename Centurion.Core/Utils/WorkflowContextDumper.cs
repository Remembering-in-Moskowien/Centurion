using Centurion.Models.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Centurion.Core.Utils;

/// <summary>
/// 将字幕工作流上下文（配置 + 各阶段状态 + 诊断信息）序列化为富上下文 JSON 文件，
/// 供调试复盘、二次加工与自动化工具消费。
/// </summary>
public static class WorkflowContextDumper
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    /// <summary>
    /// 把工作流上下文连同运行元信息写入与 <paramref name="assOutputPath"/> 同名的 .context.json 文件。
    /// </summary>
    /// <param name="context">字幕工作流上下文，包含配置与各阶段运行状态。</param>
    /// <param name="commandName">触发本次运行的子命令名（如 "spawn"）。</param>
    /// <param name="assOutputPath">本次生成的 ASS 字幕文件路径，决定 JSON 文件的输出位置与文件名。</param>
    /// <param name="cancellationToken">用于取消写文件的取消令牌。</param>
    /// <returns>写出的 JSON 文件完整路径。</returns>
    public static async Task<string> WriteAsync(
        SubtitleWorkflowContext context,
        string commandName,
        string assOutputPath,
        CancellationToken cancellationToken)
    {
        var dumpPath = Path.ChangeExtension(assOutputPath, ".context.json");
        var json = BuildJson(context, commandName, assOutputPath);
        await File.WriteAllTextAsync(dumpPath, json, cancellationToken);
        return dumpPath;
    }

    /// <summary>
    /// 构造富上下文 JSON 字符串（internal，便于单元测试）。
    /// 结构：meta（命令/时间/输入输出） + config（完整工作流配置） + state（各阶段句子、标志与诊断）。
    /// </summary>
    /// <param name="context">字幕工作流上下文。</param>
    /// <param name="commandName">触发本次运行的子命令名。</param>
    /// <param name="assOutputPath">本次生成的 ASS 字幕文件路径。</param>
    /// <returns>格式化后的 JSON 字符串。</returns>
    internal static string BuildJson(SubtitleWorkflowContext context, string commandName, string assOutputPath)
    {
        var payload = new
        {
            Meta = new
            {
                Command = commandName,
                GeneratedAt = DateTimeOffset.Now.ToString("O"),
                InputFile = context.Config.InputFilePath,
                OutputFile = assOutputPath
            },
            Config = context.Config,
            State = context.State
        };

        return JsonConvert.SerializeObject(payload, Settings);
    }
}
