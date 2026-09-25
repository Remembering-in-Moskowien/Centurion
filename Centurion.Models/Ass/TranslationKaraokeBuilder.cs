using System.Text;
using Centurion.Models.Text;

namespace Centurion.Models.Ass;

/// <summary>
/// 翻译句词级时间戳构建器：在缺少源语言词级对齐信息时，
/// 通过时间插值为译文构建卡拉OK时间戳（ASS \K 标签，单位厘秒）。
/// 分配策略：行首保留一段空拍（lead），剩余时长按"词权重"比例分配给各词——
/// 明显长音节（字符更多/音节更多）的词分得更多时间，与参考字幕（Theme.ass）的 \K 节奏风格一致。
/// 输出格式：{\K空拍}{\K词1时长}词1{\K词2时长}词2...
/// </summary>
public static class TranslationKaraokeBuilder
{
    /// <summary>
    /// 为译文构建词级 \K 时间戳文本。
    /// </summary>
    /// <param name="text">译文文本。</param>
    /// <param name="startMs">句子起始时间（毫秒）。</param>
    /// <param name="endMs">句子结束时间（毫秒）。</param>
    /// <param name="language">目标语言代码（保留用于兼容；分词以文本实际字符为准）。</param>
    /// <returns>带 \K 标签的 ASS 文本。</returns>
    public static string Build(string? text, double startMs, double endMs, string? language)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return text;

        var totalMs = Math.Max(1.0, endMs - startMs);
        var leadMs = (int)Math.Clamp(totalMs * 0.12, 300, 1000);
        var availableMs = Math.Max(1.0, totalMs - leadMs);

        var weights = tokens.Select(WeightOf).ToList();
        var weightSum = Math.Max(1, weights.Sum());

        var sb = new StringBuilder();
        sb.Append("{\\K").Append(Math.Max(1, (int)Math.Round(leadMs / 10.0))).Append('}');
        for (var i = 0; i < tokens.Count; i++)
        {
            var durationMs = Math.Max(1.0, availableMs * weights[i] / weightSum);
            sb.Append("{\\K").Append(Math.Max(1, (int)Math.Round(durationMs / 10.0))).Append('}')
              .Append(tokens[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 混合感知分词：中日韩表意/假名逐字拆分，拉丁语按空白拆词，中英混写共存；
    /// CJK 标点附着到前一词。例如 "hello 世界" → hello / 世 / 界。
    /// </summary>
    /// <param name="text">译文文本。</param>
    /// <returns>分词结果列表。</returns>
    internal static List<string> Tokenize(string text) => LanguageSupport.TokenizeMixed(text);

    /// <summary>
    /// 词权重：含中日韩表意/假名的词（或单字符）每字符权重 1；
    /// 拉丁语系按音节估算（元音字母串计数，至少 1）。长音节词因此获得更多时间分配。
    /// </summary>
    /// <param name="token">单个词或字符。</param>
    /// <returns>权重值（≥1）。</returns>
    internal static int WeightOf(string token)
    {
        if (token.Any(LanguageSupport.IsCjkIdeograph))
            return Math.Max(1, token.Length);

        // 音节粗估：连续元音字母串的数量（"adventure" → 4，"strength" → 1）
        var syllableCount = 0;
        var inVowelRun = false;
        foreach (var c in token)
        {
            if ("aeiouyAEIOUY".Contains(c))
            {
                if (!inVowelRun)
                {
                    syllableCount++;
                    inVowelRun = true;
                }
            }
            else
            {
                inVowelRun = false;
            }
        }

        return Math.Max(1, syllableCount);
    }
}
