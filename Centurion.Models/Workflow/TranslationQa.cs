using System.Text.Json.Serialization;

namespace Centurion.Models.Workflow;

/// <summary>
/// 翻译 QA 指标（TranslationOperator 产出，随 .quality.json 持久化）。
/// </summary>
public class TranslationQa
{
    /// <summary>术语命中率（0~1：含目标术语的译文句子数 / 需要命中术语的句子数；无术语表时为 1）。</summary>
    public double GlossaryHitRate { get; set; } = 1;

    /// <summary>含目标术语的译文句子数。</summary>
    public int GlossaryHits { get; set; }

    /// <summary>源句含术语、需要译文命中的句子数。</summary>
    public int GlossaryExpected { get; set; }

    /// <summary>译文/原文长度比均值（&gt;1 偏长，&lt;1 偏短；以非空白字符计）。</summary>
    public double MeanLengthRatio { get; set; } = 1;

    /// <summary>长度比偏离 1.0 的平均绝对偏差。</summary>
    public double LengthDeviation { get; set; }

    /// <summary>按句缓存命中句数（--translation-cache 时）。</summary>
    public int CachedSentenceCount { get; set; }

    /// <summary>翻译句子数。</summary>
    public int TranslatedCount { get; set; }
}
