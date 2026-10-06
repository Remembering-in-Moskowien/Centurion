using Centurion.Core.Workflow.Pipeline;using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Alignment between script sentences and the transcribed timeline.
///
/// Segmentation strategy (the only rule):
///   • Output segmentation strictly follows script sentences (<c>CurrentSentences</c>); one script line → one output line;
///   • No aggregation (merging adjacent script lines), no splitting (breaking a single script line);
///   • Transcription is only a source of timestamps and correction text; its own line breaks do not affect final segmentation.
///
/// Alignment pipeline (four stages):
///   1. Word-level NW global alignment (exact matches preferred as "anchors");
///   2. Unmatched script lines share transcript words evenly within the gaps between neighboring matches;
///   3. "Boundary leftover words" still uncovered by any line are assigned back to the nearest neighboring line;
///   4. Second-pass check of output time monotonicity.
///
/// Scaling strategy is provided by the base class <see cref="TimelineAlignmentOperatorBase{TSelf}"/>.
/// </summary>
public sealed class ScriptTimelineMapperOperator : TimelineAlignmentOperatorBase<ScriptTimelineMapperOperator>
{
    private readonly ILogger<ScriptTimelineMapperOperator> _logger;

    /// <summary>Creates a script timeline mapping operator instance.</summary>
    /// <param name="logger">Logger that records the mapping process.</param>
    public ScriptTimelineMapperOperator(ILogger<ScriptTimelineMapperOperator> logger) : base(logger)
    {
        _logger = logger;
    }

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Script Timeline Mapping";

    /// <summary>
    /// Aligns script sentences with the transcribed word stream: using script sentences as the
    /// segmentation skeleton, runs word-level global alignment and gap filling, injects timestamped words into each script sentence, and writes the result back to workflow state.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing script sentences and the transcribed word stream.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the mapping process.</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // ====== Script sentences: the sole basis for output segmentation ======
        var scriptSentences = context.State.CurrentSentences;
        if (scriptSentences.Count == 0)
        {
            const string message = "No current sentences are available for script timeline mapping.";
            context.State.Errors.Add(message);
            _logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        // ====== Transcription: flatten Words into a word stream ======
        var transcriptWords = new List<Word>();
        foreach (var s in context.State.TranscribeSentences)
            if (s.Words is { Count: > 0 })
                transcriptWords.AddRange(s.Words);

        if (transcriptWords.Count == 0)
        {
            context.State.MapperCoverage = 0;
            foreach (var sentence in scriptSentences)
            {
                ResetSentence(sentence);
                sentence.SkipRender = true;
            }

            context.State.CoarseSentences = scriptSentences;
            context.State.CurrentSentences = scriptSentences;
            _logger.LogWarning("Script mapping skipped because transcript words are empty.");
            return Task.CompletedTask;
        }

        // ==== Stage 1: word-level NW global alignment ====
        var alignment = AlignSentencesToTranscript(
            scriptSentences, transcriptWords, cancellationToken);

        var trueMatchedCount = 0;
        for (var i = 0; i < alignment.Length; i++)
            if (alignment[i].Start >= 0) trueMatchedCount++;

        // ==== Stage 2: unmatched script lines share transcript words within neighbor gaps ====
        FillUnmatchedFromGaps(alignment, transcriptWords.Count);

        // ==== Stage 3: assign still-uncovered transcript words back to matched neighbors ====
        FillRemainingGaps(alignment, transcriptWords.Count);

        // ==== Stage 4: emit output built on the script-sentence skeleton ====
        for (var i = 0; i < scriptSentences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sentence = scriptSentences[i];
            ResetSentence(sentence);

            var range = alignment[i];
            if (range.Start >= 0 && range.End >= range.Start)
            {
                for (var k = range.Start; k <= range.End && k < transcriptWords.Count; k++)
                {
                    var word = transcriptWords[k];
                    sentence.Words.Add(new Word
                    {
                        Text = word.Text,
                        Start = word.Start,
                        End = word.End,
                        Speaker = string.IsNullOrEmpty(word.Speaker) ? "UNKNOWN" : word.Speaker,
                        Status = MappingStatus.Matched
                    });
                }
            }
            else
            {
                foreach (var (original, _) in BuildScriptTokens(GetSentenceText(sentence)))
                {
                    sentence.Words.Add(new Word
                    {
                        Text = original,
                        Start = 0,
                        End = 0,
                        Speaker = "UNKNOWN",
                        Status = MappingStatus.ScriptMissing
                    });
                }
            }

            var timedWords = sentence.Words.Where(w => w.End > w.Start).ToList();
            if (timedWords.Count > 0)
            {
                sentence.Start = timedWords.Min(w => w.Start);
                sentence.End = timedWords.Max(w => w.End);
            }

            if (sentence.End <= sentence.Start)
                sentence.SkipRender = true;
        }

        // ==== Stage 5: second-pass time monotonicity check ====
        EnforceMonotonicTime(scriptSentences);

        for (var i = 0; i < scriptSentences.Count; i++)
        {
            var sentence = scriptSentences[i];
            _logger.LogInformation(
                "Mapped script sentence {Index}/{Total}: Start={Start:F0}ms End={End:F0}ms Matched={Matched} SkipRender={SkipRender}",
                i + 1, scriptSentences.Count, sentence.Start, sentence.End,
                alignment[i].Start >= 0, sentence.SkipRender);
        }

        context.State.MapperCoverage = (double)trueMatchedCount / scriptSentences.Count;
        context.State.CoarseSentences = scriptSentences;
        context.State.CurrentSentences = scriptSentences;

        if (context.State.MapperCoverage < context.Config.CoverageThreshold)
        {
            var warning =
                $"Script mapping coverage {context.State.MapperCoverage:P1} is below threshold {context.Config.CoverageThreshold:P1}.";
            context.State.Warnings.Add(warning);
            _logger.LogWarning(warning);
        }
        else
        {
            _logger.LogInformation("Script mapping coverage: {Coverage:P1}.", context.State.MapperCoverage);
        }

        OnProgress(100, $"Mapped script with {context.State.MapperCoverage:P1} coverage.");
        return Task.CompletedTask;
    }

    /// <summary>Runs word-level NW alignment between a sentence set and transcript words, returning sentence-level (Start, End) aggregates.</summary>
    private (int Start, int End)[] AlignSentencesToTranscript(
        IList<Sentence> sentences,
        IList<Word> transcriptWords,
        CancellationToken cancellationToken)
    {
        var m = sentences.Count;
        var n = transcriptWords.Count;

        var alignment = new (int Start, int End)[m];
        for (var i = 0; i < m; i++) alignment[i] = (-1, -1);
        if (n == 0 || m == 0) return alignment;

        // Flatten the script word stream
        var scriptWordNorm = new List<string>();
        var scriptWordOwner = new List<int>();
        for (var i = 0; i < m; i++)
        {
            foreach (var token in ExtractTokens(GetSentenceText(sentences[i])))
            {
                var norm = NormalizeWord(token);
                if (string.IsNullOrEmpty(norm)) continue;
                scriptWordNorm.Add(norm);
                scriptWordOwner.Add(i);
            }
        }

        if (scriptWordNorm.Count == 0) return alignment;

        var transcriptNorm = new string[n];
        for (var j = 0; j < n; j++)
            transcriptNorm[j] = NormalizeWord(transcriptWords[j].Text);

        var scriptToTranscript = AlignByWordLevelNw(scriptWordNorm, transcriptNorm, cancellationToken);
        return AggregateToSentenceAlignment(scriptToTranscript, scriptWordOwner, m);
    }

    private static void ResetSentence(Sentence sentence)
    {
        sentence.Words.Clear();
        sentence.Start = 0;
        sentence.End = 0;
        sentence.SkipRender = false;
    }
}
