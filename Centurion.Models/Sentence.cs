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
    /// <summary>句子起始时间（秒）。</summary>
    public double Start { get; set; }
    /// <summary>句子结束时间（秒）。</summary>
    public double End { get; set; }
    /// <summary>是否在渲染字幕时跳过该句子。</summary>
    public bool SkipRender { get; set; }
    /// <summary>该句包含的词级明细列表，无词级时间轴时为空。</summary>
    public List<Word> Words { get; set; } = [];
}
