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
