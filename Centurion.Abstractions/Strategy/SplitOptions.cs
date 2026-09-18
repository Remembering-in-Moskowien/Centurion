namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 分句策略配置参数（策略契约的一部分）
/// </summary>
public class SplitOptions
{
    public int MaxLength { get; set; } = 80; // 字符数上限
    public int TargetLength { get; set; } = 50; // 目标字符数
    public double MaxDuration { get; set; } = 8.0; // 最大持续秒数
    public double MinDuration { get; set; } = 0.8; // 最小持续秒数
    public int MaxWordsPerLine { get; set; } = 12; // 单词数上限
    public double MergeGap { get; set; } = 1.5; // 合并短句的时间间隙（秒）
    public bool EnablePunctuationRewrite { get; set; } = true;
    public string Language { get; set; } = "en";
    public string ModelCachePath { get; set; } = string.Empty;
    public int SpreadRange { get; set; }
    public float ChunkGranularity { get; set; } = 0.5f;
    public bool EnableResegmentation { get; set; } = false;
}
