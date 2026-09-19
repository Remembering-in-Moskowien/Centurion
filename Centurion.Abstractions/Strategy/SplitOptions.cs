namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 分句策略配置参数（策略契约的一部分）
/// </summary>
public class SplitOptions
{
    /// <summary>
    /// 单行字符数上限。
    /// </summary>
    public int MaxLength { get; set; } = 80; // 字符数上限

    /// <summary>
    /// 期望的单行目标字符数。
    /// </summary>
    public int TargetLength { get; set; } = 50; // 目标字符数

    /// <summary>
    /// 单句最大持续时长（秒）。
    /// </summary>
    public double MaxDuration { get; set; } = 8.0; // 最大持续秒数

    /// <summary>
    /// 单句最小持续时长（秒），过短的句子会尝试与相邻句合并。
    /// </summary>
    public double MinDuration { get; set; } = 0.8; // 最小持续秒数

    /// <summary>
    /// 单行最多容纳的单词数。
    /// </summary>
    public int MaxWordsPerLine { get; set; } = 12; // 单词数上限

    /// <summary>
    /// 合并相邻短句时允许的时间间隙（秒）。
    /// </summary>
    public double MergeGap { get; set; } = 1.5; // 合并短句的时间间隙（秒）

    /// <summary>
    /// 是否启用标点符号重写。
    /// </summary>
    public bool EnablePunctuationRewrite { get; set; } = true;

    /// <summary>
    /// 分句所使用的语言代码（如 "en"、"zh"）。
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// 分句模型的本地缓存目录路径。
    /// </summary>
    public string ModelCachePath { get; set; } = string.Empty;

    /// <summary>
    /// 行长度分布的扩散范围。
    /// </summary>
    public int SpreadRange { get; set; }

    /// <summary>
    /// NLP 分块粒度（0.0–1.0，越大切分越细）。
    /// </summary>
    public float ChunkGranularity { get; set; } = 0.5f;

    /// <summary>
    /// 是否启用二次重切分。
    /// </summary>
    public bool EnableResegmentation { get; set; } = false;
}
