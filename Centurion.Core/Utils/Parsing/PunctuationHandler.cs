using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 标点处理工具：去除标点符号，以及根据意群边界为无标点文本重写句读标点。
/// </summary>
public static class PunctuationHandler
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly HashSet<char> SentenceEndPunctuations = ['.', '?', '!'];
    private static readonly HashSet<char> InternalPunctuations = [',', ';', ':'];

    /// <summary>
    /// 移除所有标点符号（保留字母、数字、空格）
    /// </summary>
    public static string RemoveAllPunctuation(string text)
    {
        return PunctuationRegex.Replace(text, "");
    }

    /// <summary>
    /// 为句子重写标点：句尾添加 . 或 ? / !，内部根据意群边界插入逗号。
    /// </summary>
    /// <param name="text">无标点文本</param>
    /// <param name="phraseBoundaries">意群边界索引（字符位置）</param>
    /// <param name="isQuestion">是否为疑问句</param>
    public static string RewritePunctuation(string text, List<int> phraseBoundaries, bool isQuestion = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 内部标点：在边界处插入逗号（如果当前位置不是空格）
        var sb = new StringBuilder(text);
        var offset = 0;
        foreach (var pos in phraseBoundaries.OrderBy(p => p))
            if (pos >= 0 && pos < sb.Length && !char.IsWhiteSpace(sb[pos - 1 + offset]))
            {
                sb.Insert(pos + offset, ", ");
                offset += 2;
            }

        // 句尾标点
        var endPunct = isQuestion ? "?" : ".";
        // 如果末尾已有标点则覆盖，否则追加
        var final = sb.ToString().TrimEnd();
        if (final.Length > 0)
        {
            var last = final[^1];
            if (SentenceEndPunctuations.Contains(last))
                final = final[..^1] + endPunct;
            else if (!char.IsPunctuation(last))
                final += endPunct;
        }

        return final;
    }
}