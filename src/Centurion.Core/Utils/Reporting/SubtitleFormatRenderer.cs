using System.Text;
using System.Text.RegularExpressions;
using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// Subtitle format renderer: renders the workflow context into SRT / TXT text.
/// ASS rendering is handled by <c>AssSubBuilder</c>; this class covers the other two formats
/// supported by the build command, reusing the same bilingual / speaker / skip rules as ASS,
/// with the timeline taken from the sentence-level millisecond timestamps.
/// </summary>
public static partial class SubtitleFormatRenderer
{
    /// <summary>
    /// Renders the workflow context into SRT subtitle text (UTF-8, BOM-friendly: the caller decides the encoding).
    /// Each sentence: a line number + a standard SRT timeline + text; in bilingual mode the original and
    /// translation are joined by a newline.
    /// </summary>
    /// <param name="context">Workflow context (containing sentences and config).</param>
    /// <returns>The SRT text content.</returns>
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
    /// Renders the workflow context into plain text (one line per sentence; in bilingual mode the
    /// original and translation each take a line).
    /// </summary>
    /// <param name="context">Workflow context (containing sentences and config).</param>
    /// <returns>The plain-text content.</returns>
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

    /// <summary>Renderable sentences: skips flagged sentences and invalid timelines.</summary>
    private static IEnumerable<Sentence> GetRenderableSentences(SubtitleWorkflowContext context)
    {
        var sentences = context.State.CurrentSentences ?? [];
        return sentences
            .Where(s => !s.SkipRender && s.End >= s.Start);
    }

    /// <summary>Zero-duration lines are padded to a minimum 80ms, consistent with ASS rendering.</summary>
    private static double NormalizeStart(Sentence sentence) => sentence.Start;

    private static double NormalizeEnd(Sentence sentence, double start) =>
        sentence.End <= start ? start + 80 : sentence.End;

    /// <summary>
    /// Takes the sentence's display text, consistent with ASS: when a translation exists, choose by
    /// bilingual/monolingual mode; the speaker label is an optional prefix.
    /// Strips any leftover ASS override tags ({\...}, \N, \h, etc.) so SRT/TXT stay plain text.
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

    /// <summary>Milliseconds → SRT timeline (HH:MM:SS,mmm).</summary>
    private static string ToSrtTime(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    /// <summary>Strips ASS override tags ({\...}) and inline escapes such as \N, \h, \K, returning plain text.</summary>
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
