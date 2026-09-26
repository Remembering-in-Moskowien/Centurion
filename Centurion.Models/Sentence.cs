namespace Centurion.Models;

/// <summary>一个句子级别的转录单元，承载原文、清洗后文本、时间轴及其词级明细。</summary>
public class Sentence
{
    /// <summary>句子原始文本。</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>翻译后的文本（翻译子命令填充），为空表示未翻译。</summary>
    public string? TranslatedText { get; set; }
    /// <summary>经过文本清洗（去标点、数字展开等）后的文本，未清洗时为 null。</summary>
    public string? CleanedText { get; set; }
    /// <summary>句子起始时间（毫秒）。</summary>
    public double Start { get; set; }
    /// <summary>句子结束时间（毫秒）。</summary>
    public double End { get; set; }
    /// <summary>是否在渲染字幕时跳过该句子。</summary>
    public bool SkipRender { get; set; }
    /// <summary>该句包含的词级明细列表，无词级时间轴时为空。</summary>
    public List<Word> Words { get; set; } = [];
    /// <summary>
    /// 绑定的 ASS 样式名（对话行 Style 列）。为空时渲染端回退到默认样式；
    /// 由 convert 解析 ASS 时填充、build 渲染 ASS 时消费，供 Studio 前端逐行指定样式。
    /// </summary>
    public string? Style { get; set; }

    /// <summary>
    /// 句子说话人：从词级 <see cref="Word.Speaker"/> 按多数投票推导。
    /// 句子无词级数据或全部为占位标签（如未运行说话人分割）时返回 null；
    /// 是否把结果渲染进字幕由工作流状态（IsDiarized）与显示开关决定。
    /// </summary>
[System.Text.Json.Serialization.JsonIgnore]
    public string? Speaker
    {
        get
        {
            if (Words.Count == 0)
                return null;

            return Words
                .Select(word => word.Speaker)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .GroupBy(speaker => speaker, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault()?.Key;
        }
    }
}
