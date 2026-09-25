namespace Centurion.Models.Workflow;

/// <summary>
/// 一次拼写检查发现的疑点：可疑词、所在句子与候选建议。
/// </summary>
public sealed class SpellCheckIssue
{
    /// <summary>可疑词在句子中的序号（从 0 开始）。</summary>
    public int SentenceIndex { get; init; }

    /// <summary>可疑词原文。</summary>
    public string Word { get; init; } = "";

    /// <summary>可疑词所在句子文本。</summary>
    public string Context { get; init; } = "";

    /// <summary>Hunspell 给出的候选建议（最多 5 条）。</summary>
    public List<string> Suggestions { get; init; } = [];
}
