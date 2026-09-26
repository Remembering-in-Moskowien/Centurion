using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 双语字幕解析算子（dub 专用）：解析主字幕（源语言或目标语言），
/// 并在提供译文轨文件时按时间窗把译文匹配到句子（存 Sentence.TranslatedText）。
/// 无译文轨时整个文件视为已翻译字幕，直接以句子文本为配音文本。
/// </summary>
public sealed class BilingualSubtitleParserOperator(ILogger<BilingualSubtitleParserOperator> logger)
    : PipelineOperatorBase<BilingualSubtitleParserOperator>(logger)
{
    /// <summary>译文匹配时间窗（毫秒）：|主句中心 - 译文中心| 小于该值即视为对应句。</summary>
    private const double MatchWindowMs = 1500;

    /// <summary>算子名称。</summary>
    public override string Name => "Bilingual Subtitle Parse";

    /// <summary>
    /// 解析输入字幕并（可选）按时间窗匹配译文轨。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var subtitlePath = context.Config.SubtitleFilePath ?? context.Config.InputFilePath;
        if (!File.Exists(subtitlePath))
            throw new FileNotFoundException($"Subtitle file not found: {subtitlePath}", subtitlePath);

        var sentences = await ParseAsync(subtitlePath, cancellationToken);
        if (sentences.Count == 0)
            throw new InvalidOperationException("No subtitle items parsed.");

        // 译文轨匹配
        var translationPath = context.Config.TranslationSubtitlePath;
        if (!string.IsNullOrWhiteSpace(translationPath) && File.Exists(translationPath))
        {
            var translated = await ParseAsync(translationPath, cancellationToken);
            MatchTranslations(sentences, translated);
            context.State.Warnings.Add($"Matched {sentences.Count(s => s.TranslatedText is not null)}/{sentences.Count} translated sentences.");
        }
        else if (string.IsNullOrWhiteSpace(translationPath))
        {
            // 无译文轨：整文件即目标语言，配音文本 = 句子文本
        }
        else
        {
            context.State.Warnings.Add($"Translation subtitle file not found: {translationPath}; using main subtitle text for dubbing.");
        }

        context.State.SubtitleSentences = sentences;
        context.State.CurrentSentences = sentences;
    }

    /// <summary>解析字幕文件为句子（时间单位毫秒，与全项目一致）。</summary>
    internal static async Task<List<Sentence>> ParseAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var subtitle = SubtitleParser.ParseStream(stream)?.Subtitles ?? [];
        return subtitle
            .Where(item => item.Lines.Count > 0)
            .Select(item => new Sentence
            {
                Text = string.Join(" ", item.Lines).Trim(),
                Start = item.StartTime,
                End = item.EndTime
            })
            .ToList();
    }

    /// <summary>按时间窗把译文句匹配到主句（中心点距离最小且小于窗口）。</summary>
    internal static void MatchTranslations(List<Sentence> source, List<Sentence> translations)
    {
        foreach (var sentence in source)
        {
            var center = (sentence.Start + sentence.End) / 2.0;
            var best = translations
                .Where(t => Math.Abs((t.Start + t.End) / 2.0 - center) <= MatchWindowMs)
                .OrderBy(t => Math.Abs((t.Start + t.End) / 2.0 - center))
                .FirstOrDefault();
            if (best is not null)
                sentence.TranslatedText = best.Text;
        }
    }
}
