using System.Text.Json.Serialization;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Providers;

namespace Centurion.Models.Workflow;

/// <summary>
/// 工作流状态（可变，合并了所有 Response 的数据字段）。
/// 字段分层契约：
/// - 正式字段：被多个算子/命令链共享的阶段产物与标志，随 IR 持久化（检查点恢复）。
/// - 运行时字段（[JsonIgnore]）：进程内强类型数据，跨算子共享但不写入 IR。
/// - Extensions：单算子私有/边缘数据的通用临时槽，不写入 IR。
/// </summary>
public class WorkflowState
{
    // ---------- 音频准备链（各阶段路径由上游算子产出、下游算子消费） ----------
    /// <summary>本流水线专用的临时工作目录。</summary>
    public string? PipelineTempDirectory { get; set; }
    /// <summary>FFmpeg 重采样/转换后的临时文件路径。</summary>
    public string? ConvertedAudioPath { get; set; }
    /// <summary>完成预处理（重采样/降噪/归一化等）后的音频路径。</summary>
    public string? PreprocessedAudioPath { get; set; }
    /// <summary>Demucs 人声分离后的人声轨路径（转录/说话人分割优先消费）。</summary>
    public string? VocalsPath { get; set; }

    // ---------- VAD（语音活动检测：器乐/静音剔除 + 聚合） ----------
    /// <summary>VAD 检出的语音段（含聚合位置映射）；VadFilter 算子写入，转写后按映射还原时间轴。</summary>
    public List<VoiceSegment> VoiceSegments { get; set; } = [];
    /// <summary>VAD 聚合后的连续语音音频路径；VadFilter 算子写入，人声分离/转录优先消费。</summary>
    public string? VadAggregatedPath { get; set; }
    /// <summary>VAD 剔除的器乐/静音总时长（秒，诊断用；进程内，不写入 IR）。</summary>
    [JsonIgnore]
    public double VadRemovedSeconds { get; set; }

    // ---------- 各阶段句子列表（词级 Speaker/时间戳由下游算子逐步补充） ----------
    /// <summary>刚转录完成的句子（尚无说话人信息）。</summary>
    public List<Sentence> TranscribeSentences { get; set; } = [];
    /// <summary>作为校正基线的原始字幕句子。</summary>
    public List<Sentence> SubtitleSentences { get; set; } = [];
    /// <summary>经脚本/字幕校正后的句子。</summary>
    public List<Sentence> CorrectedSentences { get; set; } = [];
    /// <summary>分句（合并/切分）后的句子（尚无说话人信息）。</summary>
    public List<Sentence> SplitSentences { get; set; } = [];
    /// <summary>完成说话人标注的句子（每个 Word 带 Speaker）。</summary>
    public List<Sentence> DiarizedSentences { get; set; } = [];
    /// <summary>完成词级强制对齐的句子（时间戳已修正）。</summary>
    public List<Sentence> AlignedSentences { get; set; } = [];
    /// <summary>粗略（未精排）阶段的句子，可为空。</summary>
    public List<Sentence>? CoarseSentences { get; set; }
    /// <summary>导入的脚本/文稿句子。</summary>
    public List<Sentence> ScriptSentences { get; set; } = [];
    /// <summary>当前阶段正在处理的句子集合，作为下游算子的输入。</summary>
    public List<Sentence> CurrentSentences { get; set; } = [];
    /// <summary>脚本与音频词映射的实际覆盖率（0-1），供质量报告与阈值告警使用。</summary>
    public double MapperCoverage { get; set; }

    // ---------- 翻译（可选） ----------
    /// <summary>翻译后的句子列表（译文逐句写回 Sentence.TranslatedText），未启用翻译时为 null。</summary>
    public List<Sentence>? TranslatedSentences { get; set; }

    /// <summary>翻译 QA 指标（术语命中率/长度偏差等；TranslationOperator 写入，质量报告消费）。</summary>
    public TranslationQa? TranslationQa { get; set; }

    // ---------- 阶段完成标志（检查点恢复 + IR provenance 推导） ----------
    /// <summary>音频转换阶段是否已完成。</summary>
    public bool IsAudioConverted { get; set; }
    /// <summary>人声分离阶段是否已完成。</summary>
    public bool IsVocalsSeparated { get; set; }
    /// <summary>转录阶段是否已完成。</summary>
    public bool IsTranscribed { get; set; }
    /// <summary>分句阶段是否已完成。</summary>
    public bool IsSplit { get; set; }
    /// <summary>说话人分割阶段是否已完成。</summary>
    public bool IsDiarized { get; set; }
    /// <summary>强制对齐阶段是否已完成。</summary>
    public bool IsAligned { get; set; }
    /// <summary>翻译阶段是否已完成。</summary>
    public bool IsTranslated { get; set; }

    // ---------- 译制（dub）数据 ----------
    /// <summary>译制分段（说话人画像 + TTS 合成 + 时间对齐 + 混音的段级数据）。</summary>
    public List<DubSegment> DubSegments { get; set; } = [];
    /// <summary>最终译制 wav 输出路径（DubCommand 写入，AudioMix 输出与命令收尾消费）。</summary>
    public string? DubOutputWavPath { get; set; }
    /// <summary>说话人 → 参考音频路径映射（SpeakerProfiling 产出，TTS 合成消费）。</summary>
    public Dictionary<string, string> DubSpeakerReferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---------- ASS 样式表 ----------
    /// <summary>
    /// ASS 样式表（[V4+ Styles] 区）。convert 解析 ASS 时写入；build 渲染 ASS 时优先使用；
    /// 为空时渲染端回退到内置默认样式。由 Studio 前端通过中间文件编辑。
    /// </summary>
    public List<AssStyle> Styles { get; set; } = [];

    // ---------- 诊断信息 ----------
    /// <summary>运行过程中累积的错误信息列表。</summary>
    public List<string> Errors { get; set; } = [];
    /// <summary>运行过程中累积的警告信息列表。</summary>
    public List<string> Warnings { get; set; } = [];
    /// <summary>校正阶段产出的统计报告。</summary>
    public CorrectionReport Report { get; set; } = new();

    // ---------- 运行时字段（跨算子共享的进程内数据，不写入 IR） ----------
    /// <summary>管线各算子执行耗时（PipelineExecutor 写入，校正报告等消费）。</summary>
    [JsonIgnore]
    public Dictionary<string, TimeSpan> StepTimings { get; set; } = new();

    /// <summary>校正/对齐过程元数据（按句跟踪 retimed/drift 等，Sentence 为引用键，仅进程内有效）。</summary>
    [JsonIgnore]
    public Dictionary<Sentence, Dictionary<string, object>> CorrectionMetadata { get; set; } = new();

    /// <summary>
    /// Provider 调用用量聚合（token/音频秒/缓存命中/估算成本）。
    /// 转录等经 Provider 抽象执行的算子写入；命令收尾输出运行统计。
    /// 进程内有效，不写入 IR。
    /// </summary>
    [JsonIgnore]
    public List<ProviderUsage> ProviderUsages { get; set; } = [];

    // ---------- 扩展数据槽（单算子私有/边缘数据，不写入 IR） ----------
    /// <summary>
    /// 算子间临时传递非常规数据的扩展字典。
    /// 仅供单算子私有或边缘数据使用（如音频探测诊断 SourceAudioInfo/EstimatedSnrDb、
    /// 轨道检查/拼写检查/质量报告路径等单次结果）；进程内有效，不持久化进 IR。
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, object> Extensions { get; set; } = new();
}
