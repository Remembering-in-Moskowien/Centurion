using Centurion.Core.Models;

namespace Centurion.Core.Models.Workflow;

/// <summary>
/// 工作流状态（可变，合并了所有 Response 的数据字段）
/// </summary>
public class WorkflowState
{
    // ---------- 原始音频路径（由 Config.InputFilePath 派生，但保留以便存储转换后的路径） ----------
    public string? PipelineTempDirectory { get; set; }
    public string? ConvertedAudioPath { get; set; } // FFmpeg 重采样/转换后的临时文件路径
    public string? PreprocessedAudioPath { get; set; }
    public string? VocalsPath { get; set; } // Demucs 人声分离后的人声轨（转录/说话人分割优先消费）
    public AudioProbeInfo? SourceAudioInfo { get; set; }
    public AudioProbeInfo? PreprocessedAudioInfo { get; set; }
    public double? EstimatedSnrDb { get; set; }
    public bool NoiseReductionApplied { get; set; }

    // ---------- 各阶段处理后的句子列表 ----------
    // 注意：Sentence 中的 Word 对象会逐步被下游算子补充 Speaker 和精确时间戳。
    public List<Sentence> TranscribeSentences { get; set; } = []; // 刚转录完，无说话人信息
    public List<Sentence> SubtitleSentences { get; set; } = []; // 校准输入的原始字幕基线
    public List<Sentence> CorrectedSentences { get; set; } = [];
    public List<Sentence> SplitSentences { get; set; } = []; // 分句后（合并/切分），无说话人信息
    public List<Sentence> DiarizedSentences { get; set; } = []; // 说话人标注后（每个 Word 带 Speaker）
    public List<Sentence> AlignedSentences { get; set; } = []; // 强制对齐后（词级时间戳修正）
    public List<Sentence>? CoarseSentences { get; set; }
    public List<Sentence> ScriptSentences { get; set; } = [];
    public List<Sentence> CurrentSentences { get; set; } = [];
    public double MapperCoverage { get; set; }

    // ---------- 转换专用数据槽：已移除 SubtitlesParserV2 模型，统一使用 Sentence ----------
    // （转换后的原始字幕将存入 TranscribeSentences，与转录结果同构）

    // ---------- 翻译结果（可选） ----------
    public List<Sentence>? TranslatedSentences { get; set; }

    // ---------- 阶段完成标志（用于检查点恢复） ----------
    public bool IsAudioConverted { get; set; }
    public bool IsVocalsSeparated { get; set; }
    public bool IsTranscribed { get; set; }
    public bool IsSplit { get; set; }
    public bool IsDiarized { get; set; }
    public bool IsAligned { get; set; }

    public bool IsTranslated { get; set; }

    public bool IsFinalized { get; set; }

    // ---------- 运行时诊断信息 ----------
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public CorrectionReport Report { get; set; } = new();

    // ---------- 扩展数据槽（用于算子间临时传递非常规数据，避免改上下文结构） ----------
    public Dictionary<string, object> Extensions { get; set; } = new();
}
