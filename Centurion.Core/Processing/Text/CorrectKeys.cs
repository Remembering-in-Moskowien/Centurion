namespace Centurion.Core.Processing.Text;

/// <summary>
/// 文本校正相关元数据在扩展数据字典中使用的键名常量。
/// </summary>
public static class CorrectKeys
{
    /// <summary>原始（校正前）文本来源对应的键名。</summary>
    public const string Origin = "correct.origin";
    /// <summary>时间漂移量（毫秒）对应的键名。</summary>
    public const string DriftMs = "correct.driftMs";
    /// <summary>所采取校正动作对应的键名。</summary>
    public const string Action = "correct.action";
    /// <summary>文本匹配率对应的键名。</summary>
    public const string MatchRatio = "correct.matchRatio";
}