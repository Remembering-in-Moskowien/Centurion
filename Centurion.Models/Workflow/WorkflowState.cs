using Centurion.Models;
using Centurion.Models.Ass;

namespace Centurion.Models.Workflow;

/// <summary>
/// 工作流状态（可变，合并了所有 Response 的数据字段）
/// </summary>
public class WorkflowState
{
    // ---------- 原始音频路径（由 Config.InputFilePath 派生，但保留以便存储转换后的路径） ----------
    /// <summary>本流水线专用的临时工作目录。</summary>
    public string? PipelineTempDirectory { get; set; }
    /// <summary>FFmpeg 重采样/转换后的临时文件路径。</summary>
    public string? ConvertedAudioPath { get; set; } // FFmpeg 重采样/转换后的临时文件路径
    /// <summary>完成预处理（重采样/降噪/归一化等）后的音频路径。</summary>
    public string? PreprocessedAudioPath { get; set; }
    /// <summary>Demucs 人声分离后的人声轨路径（转录/说话人分割优先消费）。</summary>
    public string? VocalsPath { get; set; } // Demucs 人声分离后的人声轨（转录/说话人分割优先消费）
    /// <summary>原始输入音频的探测信息。</summary>
    public AudioProbeInfo? SourceAudioInfo { get; set; }
    /// <summary>预处理后音频的探测信息。</summary>
    public AudioProbeInfo? PreprocessedAudioInfo { get; set; }
    /// <summary>估计的信噪比（分贝），未估计时为 null。</summary>
    public double? EstimatedSnrDb { get; set; }
    /// <summary>是否已对音频施加了降噪处理。</summary>
    public bool NoiseReductionApplied { get; set; }

    // ---------- 各阶段处理后的句子列表 ----------
    // 注意：Sentence 中的 Word 对象会逐步被下游算子补充 Speaker 和精确时间戳。
    /// <summary>刚转录完成的句子（尚无说话人信息）。</summary>
    public List<Sentence> TranscribeSentences { get; set; } = []; // 刚转录完，无说话人信息
    /// <summary>作为校正基线的原始字幕句子。</summary>
    public List<Sentence> SubtitleSentences { get; set; } = []; // 校准输入的原始字幕基线
    /// <summary>经脚本/字幕校正后的句子。</summary>
    public List<Sentence> CorrectedSentences { get; set; } = [];
    /// <summary>分句（合并/切分）后的句子（尚无说话人信息）。</summary>
    public List<Sentence> SplitSentences { get; set; } = []; // 分句后（合并/切分），无说话人信息
    /// <summary>完成说话人标注的句子（每个 Word 带 Speaker）。</summary>
    public List<Sentence> DiarizedSentences { get; set; } = []; // 说话人标注后（每个 Word 带 Speaker）
    /// <summary>完成词级强制对齐的句子（时间戳已修正）。</summary>
    public List<Sentence> AlignedSentences { get; set; } = []; // 强制对齐后（词级时间戳修正）
    /// <summary>粗略（未精排）阶段的句子，可为空。</summary>
    public List<Sentence>? CoarseSentences { get; set; }
    /// <summary>导入的脚本/文稿句子。</summary>
    public List<Sentence> ScriptSentences { get; set; } = [];
    /// <summary>当前阶段正在处理的句子集合，作为下游算子的输入。</summary>
    public List<Sentence> CurrentSentences { get; set; } = [];
    /// <summary>脚本与音频词映射的实际覆盖率（0-1）。</summary>
    public double MapperCoverage { get; set; }

    // ---------- 转换专用数据槽：已移除 SubtitlesParserV2 模型，统一使用 Sentence ----------
    // （转换后的原始字幕将存入 TranscribeSentences，与转录结果同构）

    // ---------- 翻译结果（可选） ----------
    /// <summary>翻译后的句子列表，未启用翻译时为 null。</summary>
    public List<Sentence>? TranslatedSentences { get; set; }

    // ---------- 阶段完成标志（用于检查点恢复） ----------
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

    // ---------- ASS 样式表 ----------
    /// <summary>
    /// ASS 样式表（[V4+ Styles] 区）。convert 解析 ASS 时写入；build 渲染 ASS 时优先使用；
    /// 为空时渲染端回退到内置默认样式。由 Studio 前端通过中间文件编辑。
    /// </summary>
    public List<AssStyle> Styles { get; set; } = [];

    /// <summary>整个流水线是否已收尾完成。</summary>
    public bool IsFinalized { get; set; }

    // ---------- 运行时诊断信息 ----------
    /// <summary>运行过程中累积的错误信息列表。</summary>
    public List<string> Errors { get; set; } = [];
    /// <summary>运行过程中累积的警告信息列表。</summary>
    public List<string> Warnings { get; set; } = [];
    /// <summary>校正阶段产出的统计报告。</summary>
    public CorrectionReport Report { get; set; } = new();

    // ---------- 扩展数据槽（用于算子间临时传递非常规数据，避免改上下文结构） ----------
    /// <summary>算子间临时传递非常规数据的扩展字典。</summary>
    public Dictionary<string, object> Extensions { get; set; } = new();
}
