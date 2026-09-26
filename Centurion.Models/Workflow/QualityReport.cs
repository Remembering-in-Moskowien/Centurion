namespace Centurion.Models.Workflow;

/// <summary>
/// 一次字幕工作流运行的统一质量报告（写出为 .quality.json）。
/// 覆盖输入规模、阶段覆盖、对齐质量与运行诊断，供各命令横向对比与自动化消费。
/// </summary>
public class QualityReport
{
    /// <summary>运行元信息。</summary>
    public QualityMeta Meta { get; set; } = new();

    /// <summary>输入与产出的规模统计。</summary>
    public QualityCounts Counts { get; set; } = new();

    /// <summary>各处理阶段是否完成及其产出规模。</summary>
    public QualityCoverage Coverage { get; set; } = new();

    /// <summary>时间轴/对齐质量指标。</summary>
    public QualityAlignment Alignment { get; set; } = new();

    /// <summary>行级时序/可读性指标（CPS、行宽、最小时长、最大时长、重叠）。</summary>
    public QualityTiming? Timing { get; set; }

    /// <summary>ASR 置信度信息（模型提供时）。</summary>
    public QualityConfidence? Confidence { get; set; }

    /// <summary>翻译 QA 指标（术语命中、长度偏差、回译相似度、缓存）。</summary>
    public QualityTranslation? Translation { get; set; }

    /// <summary>TTS/配音对齐与响度度量（dub 命令时）。</summary>
    public QualityTts? Tts { get; set; }

    /// <summary>行级问题明细（HTML 报告可逐行定位；由 QualityAssessor 生成）。</summary>
    public List<QualityLineIssue> Issues { get; set; } = [];

    /// <summary>阈值评估是否全部通过（CI 依据；--fail-on 触发失败时为 false）。</summary>
    public bool Passed { get; set; } = true;

    /// <summary>未通过的阈值规则列表（如 "cps>20"）。</summary>
    public List<string> FailedThresholds { get; set; } = [];

    /// <summary>非致命警告列表。</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>致命错误列表（为空表示本次运行成功）。</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>dub（媒体译制）专属指标；非 dub 命令为 null。</summary>
    public QualityDub? Dub { get; set; }
}

/// <summary>运行元信息。</summary>
public class QualityMeta
{
    /// <summary>子命令名。</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>报告生成时间（ISO 8601）。</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>输入文件路径。</summary>
    public string InputFile { get; set; } = string.Empty;

    /// <summary>输出文件路径。</summary>
    public string OutputFile { get; set; } = string.Empty;

    /// <summary>本次运行总耗时（秒）。</summary>
    public double ElapsedSeconds { get; set; }
}

/// <summary>输入与产出的规模统计。</summary>
public class QualityCounts
{
    /// <summary>最终句子数。</summary>
    public int SentenceCount { get; set; }

    /// <summary>词级单元总数（含标点、符号）。</summary>
    public int WordCount { get; set; }

    /// <summary>字符总数（不计空白）。</summary>
    public int CharacterCount { get; set; }

    /// <summary>识别出的说话人数（未做说话人分割时为 0 或 1）。</summary>
    public int SpeakerCount { get; set; }

    /// <summary>内容总时长（秒，末句结束 - 首句开始）。</summary>
    public double DurationSeconds { get; set; }

    /// <summary>平均语速（字符/秒）。</summary>
    public double CharactersPerSecond { get; set; }
}

/// <summary>各处理阶段是否完成及其产出规模。</summary>
public class QualityCoverage
{
    /// <summary>是否完成转录。</summary>
    public bool Transcribed { get; set; }

    /// <summary>是否完成说话人分割。</summary>
    public bool Diarized { get; set; }

    /// <summary>是否完成分句。</summary>
    public bool Split { get; set; }

    /// <summary>是否完成强制对齐。</summary>
    public bool Aligned { get; set; }

    /// <summary>是否完成翻译。</summary>
    public bool Translated { get; set; }

    /// <summary>转录句子数（存在时）。</summary>
    public int TranscribedSentenceCount { get; set; }

    /// <summary>对齐句子数（存在时）。</summary>
    public int AlignedSentenceCount { get; set; }

    /// <summary>翻译句子数（存在时）。</summary>
    public int TranslatedSentenceCount { get; set; }
}

/// <summary>时间轴与对齐质量指标。</summary>
public class QualityAlignment
{
    /// <summary>校准/映射的平均漂移（毫秒；无校准数据时为 null）。</summary>
    public double? MeanDriftMs { get; set; }

    /// <summary>最大漂移（毫秒；无校准数据时为 null）。</summary>
    public double? MaxDriftMs { get; set; }

    /// <summary>文本映射覆盖率（0~1；台本打轴等场景）。</summary>
    public double MapperCoverage { get; set; }

    /// <summary>零时长句子数（Start == End，属异常需关注）。</summary>
    public int ZeroDurationSentenceCount { get; set; }

    /// <summary>最短句时长（毫秒）。</summary>
    public double MinSentenceDurationMs { get; set; }

    /// <summary>最长句时长（毫秒）。</summary>
    public double MaxSentenceDurationMs { get; set; }
}

/// <summary>行级问题严重度。</summary>
public enum QualityIssueSeverity
{
    /// <summary>不影响播放/理解，但值得关注。</summary>
    Warning,

    /// <summary>明显影响可读性或时间轴正确性（CI 阈值通常针对此类）。</summary>
    Error
}

/// <summary>行级问题类型（与修复引擎一一对应）。</summary>
public enum QualityIssueType
{
    /// <summary>语速超过 CPS 阈值。</summary>
    CpsTooHigh,

    /// <summary>单行字符数超过行宽阈值。</summary>
    LineTooLong,

    /// <summary>句子时长低于最小时长阈值。</summary>
    TooShort,

    /// <summary>句子时长超过最大时长阈值。</summary>
    TooLong,

    /// <summary>与下一句时间轴重叠。</summary>
    Overlap,

    /// <summary>零时长（Start == End）。</summary>
    ZeroDuration,

    /// <summary>ASR 置信度低于阈值（模型提供时）。</summary>
    LowConfidence,

    /// <summary>译文未命中术语表目标词。</summary>
    GlossaryMiss,

    /// <summary>译文长度相对原文偏差过大。</summary>
    LengthDeviation,

    /// <summary>句间出现负间隙（后句开始早于前句结束，即重叠）。</summary>
    NegativeGap
}

/// <summary>一条可定位到具体字幕行的质量问题。</summary>
public class QualityLineIssue
{
    /// <summary>问题类型（QualityIssueType 名称）。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>严重度（warning / error）。</summary>
    public string Severity { get; set; } = QualityIssueSeverity.Warning.ToString();

    /// <summary>0 基句子索引（HTML 报告行号 = Index + 1）。</summary>
    public int SentenceIndex { get; set; }

    /// <summary>句子起始时间（毫秒）。</summary>
    public double StartMs { get; set; }

    /// <summary>句子结束时间（毫秒）。</summary>
    public double EndMs { get; set; }

    /// <summary>句子原文（或译文）。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>问题描述。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>建议修复方式（quality --fix 可自动应用）。</summary>
    public string? Fix { get; set; }

    /// <summary>该问题的量化值（如实际 CPS、实际字符数、重叠毫秒数）。</summary>
    public double? Value { get; set; }

    /// <summary>该问题的阈值（用于对比）。</summary>
    public double? Limit { get; set; }
}

/// <summary>时序/可读性统计（CPS、行宽、最小时长、最大时长、重叠）。</summary>
public class QualityTiming
{
    /// <summary>平均语速（字符/秒）。</summary>
    public double MeanCps { get; set; }

    /// <summary>最大句级语速（字符/秒）。</summary>
    public double MaxCps { get; set; }

    /// <summary>超过 CPS 阈值的句子数。</summary>
    public int CpsTooHighCount { get; set; }

    /// <summary>单行字符数超过行宽阈值的句子数。</summary>
    public int LineTooLongCount { get; set; }

    /// <summary>时长低于最小时长阈值的句子数。</summary>
    public int TooShortCount { get; set; }

    /// <summary>时长超过最大时长阈值的句子数。</summary>
    public int TooLongCount { get; set; }

    /// <summary>与下一句重叠的句子数。</summary>
    public int OverlapCount { get; set; }

    /// <summary>重叠总量（秒）。</summary>
    public double TotalOverlapSeconds { get; set; }

    /// <summary>句间平均停顿（秒；负值表示存在重叠）。</summary>
    public double MeanGapSeconds { get; set; }
}

/// <summary>ASR 置信度信息。</summary>
public class QualityConfidence
{
    /// <summary>平均句级置信度（0~1；模型未提供时为 null）。</summary>
    public double? MeanConfidence { get; set; }

    /// <summary>低于阈值的低置信度句子索引。</summary>
    public List<int> LowConfidenceSentenceIndexes { get; set; } = [];
}

/// <summary>翻译 QA 指标。</summary>
public class QualityTranslation
{
    /// <summary>术语命中率（0~1：命中目标术语的译文句子占比；无术语表时为 1）。</summary>
    public double GlossaryHitRate { get; set; } = 1;

    /// <summary>命中术语的句子数。</summary>
    public int GlossaryHits { get; set; }

    /// <summary>需要命中术语的句子数。</summary>
    public int GlossaryExpected { get; set; }

    /// <summary>译文/原文长度比均值（>1 偏长，&lt;1 偏短）。</summary>
    public double MeanLengthRatio { get; set; } = 1;

    /// <summary>长度比偏离 1.0 的平均绝对偏差。</summary>
    public double LengthDeviation { get; set; }

    /// <summary>回译相似度（0~1；未启用回译时为 null）。</summary>
    public double? BackTranslateSimilarity { get; set; }

    /// <summary>按句缓存命中句数（--translation-cache 时）。</summary>
    public int CachedSentenceCount { get; set; }
}

/// <summary>TTS/配音对齐与响度度量。</summary>
public class QualityTts
{
    /// <summary>TTS 时长预测 vs 实际对齐的平均误差（毫秒）。</summary>
    public double MeanAlignmentErrorMs { get; set; }

    /// <summary>TTS 时长预测 vs 实际对齐的最大误差（毫秒）。</summary>
    public double MaxAlignmentErrorMs { get; set; }

    /// <summary>平均语速（字符/秒，TTS 合成段）。</summary>
    public double MeanSpeechRate { get; set; }

    /// <summary>句间平均停顿（秒）。</summary>
    public double MeanPauseSeconds { get; set; }

    /// <summary>句间最大停顿（秒）。</summary>
    public double MaxPauseSeconds { get; set; }

    /// <summary>负间隙（重叠）总量（秒）；无重叠时为 0。</summary>
    public double NegativeGapSeconds { get; set; }

    /// <summary>EBU R128 综合响度（LUFS；探测失败时为 null）。</summary>
    public double? LoudnessLufs { get; set; }

    /// <summary>是否应用了 ducking（有伴奏且 DubDucking 开启时 true）。</summary>
    public bool DuckingApplied { get; set; }
}

/// <summary>dub（媒体译制）专属质量指标。</summary>
public class QualityDub
{
    /// <summary>待配音段总数（含分块子段与跳过项）。</summary>
    public int SegmentsTotal { get; set; }

    /// <summary>成功合成段数。</summary>
    public int SegmentsSucceeded { get; set; }

    /// <summary>跳过段数（TTS 失败等）。</summary>
    public int SegmentsSkipped { get; set; }

    /// <summary>翻译覆盖率（0~1：有译文文本的句子占比）。</summary>
    public double TranslationCoverage { get; set; }

    /// <summary>时间对齐平均偏差（毫秒：|目标时长 - 对齐后时长|）。</summary>
    public double MeanAlignmentDeviationMs { get; set; }

    /// <summary>时间对齐最大偏差（毫秒）。</summary>
    public double MaxAlignmentDeviationMs { get; set; }

    /// <summary>最终应用变速比最小值（atempo）。</summary>
    public double TempoMin { get; set; }

    /// <summary>最终应用变速比最大值（atempo）。</summary>
    public double TempoMax { get; set; }

    /// <summary>最终应用变速比平均值（atempo）。</summary>
    public double TempoAverage { get; set; }

    /// <summary>
    /// 克隆一致性启发式（0~1：基于合成段与目标时长偏差的节奏一致性，非真实声纹相似度）。
    /// 需要 TitaNet 等声纹 embedding 时此字段将由真实相似度替换；当前为启发式代理。
    /// </summary>
    public double CloneConsistency { get; set; }
}
