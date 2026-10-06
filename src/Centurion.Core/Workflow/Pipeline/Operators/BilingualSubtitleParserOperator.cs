using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Bilingual subtitle parser operator (dub-only): parses the main subtitle (source or target
/// language); when a translation track file is given, matches translations to sentences by time window (stored in Sentence.TranslatedText);
/// otherwise the whole file is treated as translated subtitles and its sentence text is used directly as the dub text.
/// </summary>
public sealed class BilingualSubtitleParserOperator(ILogger<BilingualSubtitleParserOperator> logger)
    : PipelineOperatorBase<BilingualSubtitleParserOperator>(logger)
{
    /// <summary>Translation matching time window (ms): |main-sentence center - translation center| below this value counts as a pair.</summary>
    private const double MatchWindowMs = 1500;

    /// <summary>Operator name.</summary>
    public override string Name => "Bilingual Subtitle Parse";

    /// <summary>
    /// Parses the input subtitle and optionally matches a translation track by time window.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var subtitlePath = context.Config.SubtitleFilePath ?? context.Config.InputFilePath;
        if (!File.Exists(subtitlePath))
            throw new FileNotFoundException($"Subtitle file not found: {subtitlePath}", subtitlePath);

        var sentences = await ParseAsync(subtitlePath, cancellationToken);
        if (sentences.Count == 0)
            throw new InvalidOperationException("No subtitle items parsed.");

        // Match the translation track
        var translationPath = context.Config.TranslationSubtitlePath;
        if (!string.IsNullOrWhiteSpace(translationPath) && File.Exists(translationPath))
        {
            var translated = await ParseAsync(translationPath, cancellationToken);
            MatchTranslations(sentences, translated);
            context.State.Warnings.Add($"Matched {sentences.Count(s => s.TranslatedText is not null)}/{sentences.Count} translated sentences.");
        }
        else if (string.IsNullOrWhiteSpace(translationPath))
        {
            // No translation track: the whole file is the target language, so dub text = sentence text
        }
        else
        {
            context.State.Warnings.Add($"Translation subtitle file not found: {translationPath}; using main subtitle text for dubbing.");
        }

        context.State.SubtitleSentences = sentences;
        context.State.CurrentSentences = sentences;
    }

    /// <summary>Parses a subtitle file into sentences (timing in milliseconds, consistent with the whole project).</summary>
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

    /// <summary>Matches translation sentences to main sentences by time window (smallest center-point distance below the window).</summary>
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
