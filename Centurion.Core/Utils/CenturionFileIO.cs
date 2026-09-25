using Centurion.Models.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Centurion.Core.Utils;

/// <summary>
/// Centurion 中间文件（*.centurion.json）的读写器。
/// 中间文件是命令链中唯一的结构化交换格式：完整序列化 <see cref="SubtitleWorkflowContext"/>
/// （不可变配置 + 各阶段句子/词级时间戳/说话人/翻译/译制分段 + 诊断），
/// 可无损反序列化回上下文供后续命令继续处理。
/// 传统字幕文件仅在 convert 进入本格式、build 离开本格式。
/// </summary>
public static class CenturionFileIO
{
    /// <summary>中间文件扩展名。</summary>
    public const string Extension = ".centurion.json";

    /// <summary>当前中间文件格式版本（结构变更时递增；读取时用于兼容校验）。</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    /// <summary>
    /// 计算中间文件的默认输出路径：输入名 + 可选后缀 + 中间文件扩展名。
    /// 后缀用于避免"输入已是中间文件"时默认输出覆盖输入（如 correct/translate/dub 链式处理）。
    /// </summary>
    /// <param name="inputPath">输入文件路径（媒体/字幕/中间文件均可）。</param>
    /// <param name="suffix">可选后缀（如 "corrected"、"translated"、"dub"）；为空时无后缀。</param>
    /// <returns>默认输出中间文件路径。</returns>
    public static string DefaultOutputPath(string inputPath, string? suffix = null)
    {
        var dir = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileName(inputPath);
        var baseName = IsCenturionFile(fileName)
            ? fileName[..^Extension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        var tail = string.IsNullOrWhiteSpace(suffix) ? string.Empty : $".{suffix}";
        var result = $"{baseName}{tail}{Extension}";
        return string.IsNullOrEmpty(dir) ? result : Path.Combine(dir, result);
    }

    /// <summary>判断路径是否为 Centurion 中间文件（按文件名尾缀 .centurion.json）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是中间文件时为 true。</returns>
    public static bool IsCenturionFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把工作流上下文完整保存为中间文件（meta 包裹 + config + state）。
    /// </summary>
    /// <param name="context">工作流上下文（含配置与全部状态）。</param>
    /// <param name="path">输出中间文件路径。</param>
    /// <param name="commandName">触发本次保存的子命令名（写入 meta）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写出的中间文件完整路径。</returns>
    public static async Task<string> SaveAsync(
        SubtitleWorkflowContext context,
        string path,
        string commandName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Meta = new
            {
                Format = "centurion",
                Version = FormatVersion,
                Command = commandName,
                GeneratedAt = DateTimeOffset.Now.ToString("O"),
                InputFile = context.Config.InputFilePath,
                OutputFile = path
            },
            Config = context.Config,
            State = context.State
        };

        var json = JsonConvert.SerializeObject(payload, Settings);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    /// <summary>
    /// 从中间文件无损恢复工作流上下文（meta 版本校验 + config + state 反序列化）。
    /// </summary>
    /// <param name="path">中间文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复的工作流上下文。</returns>
    /// <exception cref="FileNotFoundException">文件不存在时抛出。</exception>
    /// <exception cref="InvalidDataException">格式/版本不受支持时抛出。</exception>
    public static async Task<SubtitleWorkflowContext> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Centurion intermediate file not found: {path}", path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);

        CenturionFile? file;
        try
        {
            file = JsonConvert.DeserializeObject<CenturionFile>(json, Settings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{path}' is not a valid Centurion intermediate file: {ex.Message}", ex);
        }

        if (file?.Config is null || file.State is null)
            throw new InvalidDataException($"'{path}' is missing config/state — not a Centurion intermediate file.");

        if (file.Meta?.Version != FormatVersion)
            throw new InvalidDataException(
                $"Unsupported Centurion file version {file.Meta?.Version ?? 0} (expected {FormatVersion}). Re-run the generating command to upgrade.");

        return new SubtitleWorkflowContext(file.Config) { State = file.State };
    }

    /// <summary>中间文件反序列化载体（meta 包裹 + config + state）。</summary>
    private sealed class CenturionFile
    {
        /// <summary>文件元信息。</summary>
        public CenturionMeta? Meta { get; set; }

        /// <summary>不可变工作流配置。</summary>
        public WorkflowConfig? Config { get; set; }

        /// <summary>可变工作流状态。</summary>
        public WorkflowState? State { get; set; }
    }

    /// <summary>中间文件元信息。</summary>
    private sealed class CenturionMeta
    {
        /// <summary>格式名（恒为 "centurion"）。</summary>
        public string? Format { get; set; }

        /// <summary>格式版本。</summary>
        public int Version { get; set; }

        /// <summary>生成命令名。</summary>
        public string? Command { get; set; }

        /// <summary>生成时间。</summary>
        public string? GeneratedAt { get; set; }

        /// <summary>输入文件路径。</summary>
        public string? InputFile { get; set; }

        /// <summary>输出文件路径。</summary>
        public string? OutputFile { get; set; }
    }
}
