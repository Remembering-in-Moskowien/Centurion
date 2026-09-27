using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Centurion.Models.Schema;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// 从 <see cref="SubtitleWorkflowContext"/> 构造 <see cref="CenturionDocument"/>
/// （generator + provenance + config/state），供命令链保存 IR 时统一使用。
/// </summary>
public static class CenturionDocumentBuilder
{
    /// <summary>由工作流上下文与当前命令构造文档。</summary>
    public static CenturionDocument Create(
        SubtitleWorkflowContext context,
        string commandName,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CenturionDocument
        {
            Generator = new GeneratorInfo
            {
                Version = ToolVersion,
                Command = commandName,
                InputFile = context.Config.InputFilePath,
                OutputFile = outputPath
            },
            Config = context.Config,
            State = context.State,
            Provenance = BuildProvenance(context.State, context.Config)
        };
    }

    /// <summary>
    /// 依据状态标志推导已执行的处理步骤（每个算子一条溯源记录）。
    /// 步骤顺序与管道自然顺序一致：转换 → 人声分离 → 转录 → 分句 → 说话人分割 → 对齐 → 翻译 → 译制。
    /// </summary>
    private static List<ProvenanceEntry> BuildProvenance(WorkflowState state, WorkflowConfig config)
    {
        var entries = new List<ProvenanceEntry>(8);
        var hash = ParametersHash(config);
        var appliedAt = DateTimeOffset.Now.ToString("O");

        void Add(string operatorName, string? model) =>
            entries.Add(new ProvenanceEntry
            {
                Operator = operatorName,
                Model = model,
                ParametersHash = hash,
                AppliedAt = appliedAt
            });

        if (state.IsAudioConverted) Add("audio-convert", null);
        if (state.IsVocalsSeparated) Add("vocal-separation", config.VocalSeparationModel);
        if (state.IsTranscribed) Add("transcribe", config.TranscriberModel);
        if (state.IsSplit) Add("split", config.SplitterModel);
        if (state.IsDiarized) Add("diarize", config.DiarizationModel);
        if (state.IsAligned) Add("align", config.AlignmentModel);
        if (state.IsTranslated) Add("translate", config.TranslationModel);
        if (state.DubSegments.Count > 0) Add("dub", config.TtsModel);

        return entries;
    }

    /// <summary>
    /// 工作流配置的 SHA-256 指纹（十六进制）：对配置的规范化 JSON 取哈希，
    /// 配置一致则指纹一致，用于溯源"这份结果基于什么参数生成"。
    /// </summary>
    public static string ParametersHash(WorkflowConfig config)
    {
        var json = JsonSerializer.Serialize(config, CenturionJsonContext.Default.WorkflowConfig);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>当前工具版本（程序集 InformationalVersion）。</summary>
    private static string ToolVersion =>
        typeof(CenturionDocumentBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CenturionDocumentBuilder).Assembly.GetName().Version?.ToString() ?? "unknown";
}
