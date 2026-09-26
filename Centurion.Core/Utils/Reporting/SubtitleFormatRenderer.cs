using System.Text;
using System.Text.RegularExpressions;
using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// 字幕格式渲染器：把工作流上下文渲染为 SRT / TXT 文本。
/// ASS 渲染由 <c>AssSubBuilder</c> 负责；本类负责 build 命令支持的其余两种格式，
/// 复用与 ASS 一致的双语/说话人/跳过规则，时间轴取自句子级毫秒时间戳。
/// </summary>
public static partial class SubtitleFormatRenderer
{
    /// <summary>
    /// 把工作流上下文渲染为 SRT 字幕文本（UTF-8，含 BOM 友好：调用方决定编码）。
    /// 每句一行序号 + 标准 SRT 时间轴 + 文本；双语时原文与译文以换行拼接。
    /// </summary>
    /// <param name="context">工作流上下文（含句子与配置）。</param>
    /// <returns>SRT 文本内容。</returns>
    public static string RenderSrt(SubtitleWorkflowContext context)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var sentence in GetRenderableSentences(context))
        {
            var start = NormalizeStart(sentence);
            var end = NormalizeEnd(sentence, start);
            var text = GetDisplayText(sentence, context);

            sb.Append(index++)
              .Append('\n')
              .Append(ToSrtTime(start)).Append(" --> ").Append(ToSrtTime(end)).Append('\n')
              .Append(text).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把工作流上下文渲染为纯文本（每句一行；双语时原文/译文各占一行）。
    /// </summary>
    /// <param name="context">工作流上下文（含句子与配置）。</param>
    /// <returns>纯文本内容。</returns>
    public static string RenderTxt(SubtitleWorkflowContext context)
    {
        var sb = new StringBuilder();
        foreach (var sentence in GetRenderableSentences(context))
        {
            var text = GetDisplayText(sentence, context);
            sb.Append(text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>可渲染句子：跳过被标记的句子与非法时间轴。</summary>
    private static IEnumerable<Sentence> GetRenderableSentences(SubtitleWorkflowContext context)
    {
        var sentences = context.State.CurrentSentences ?? [];
        return sentences
            .Where(s => !s.SkipRender && s.End >= s.Start);
    }

    /// <summary>零时长句补最小 80ms，与 ASS 渲染保持一致。</summary>
    private static double NormalizeStart(Sentence sentence) => sentence.Start;

    private static double NormalizeEnd(Sentence sentence, double start) =>
        sentence.End <= start ? start + 80 : sentence.End;

    /// <summary>
    /// 取句子显示文本：与 ASS 一致——有译文时按双语/单语选择；说话人标签可选前缀。
    /// 清除可能残留的 ASS 覆盖标签（{\...}、\N、\h 等），保证 SRT/TXT 为纯文本。
    /// </summary>
    private static string GetDisplayText(Sentence sentence, SubtitleWorkflowContext context)
    {
        var speakerPrefix = context.State.IsDiarized && context.Config.ShowSpeakerLabels
            && !string.IsNullOrWhiteSpace(sentence.Speaker)
            ? $"[{CleanAssTags(sentence.Speaker)}] "
            : string.Empty;

        string body;
        if (!string.IsNullOrWhiteSpace(sentence.TranslatedText))
        {
            var translated = CleanAssTags(sentence.TranslatedText);
            body = context.Config.Bilingual
                ? $"{CleanAssTags(sentence.Text)}\n{translated}"
                : translated;
        }
        else
        {
            body = CleanAssTags(sentence.Text);
        }

        return string.IsNullOrEmpty(body)
            ? string.Empty
            : speakerPrefix + body;
    }

    /// <summary>毫秒 → SRT 时间轴（HH:MM:SS,mmm）。</summary>
    private static string ToSrtTime(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    /// <summary>去除 ASS 覆盖标签（{\...}）与 \N、\h、\K 等行内转义，返回纯文本。</summary>
    private static string CleanAssTags(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = AssOverridePattern().Replace(text, string.Empty);
        result = result.Replace("\\N", "\n", StringComparison.Ordinal)
                       .Replace("\\n", "\n", StringComparison.Ordinal)
                       .Replace("\\h", " ", StringComparison.Ordinal)
                       .Replace("\\K", string.Empty, StringComparison.Ordinal);
        return result.Trim();
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssOverridePattern();
}
