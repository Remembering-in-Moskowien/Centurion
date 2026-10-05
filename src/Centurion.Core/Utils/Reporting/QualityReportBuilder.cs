using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// Extracts metrics from <see cref="SubtitleWorkflowContext"/> and builds a <see cref="QualityReport"/>.
/// The statistics logic is IO-independent for easy unit testing, and is shared by all paths
/// (spawn/from-script/correct/convert/translate/dub).
/// </summary>
public static class QualityReportBuilder
{
    /// <summary>
    /// Builds a quality report from the workflow context.
    /// </summary>
    /// <param name="context">Subtitle workflow context.</param>
    /// <param name="outputPath">Output file path for this run (used for Meta).</param>
    /// <param name="elapsedSeconds">Total elapsed time for this run (seconds).</param>
    /// <returns>The quality report.</returns>
    public static QualityReport Build(SubtitleWorkflowContext context, string outputPath, double elapsedSeconds)
    {
        var state = context.State;
        var sentences = EffectiveSentences(state);

        var words = sentences.SelectMany(s => s.Words ?? []).ToList();
        var characters = sentences.Sum(s => s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0);

        var report = new QualityReport
        {
            Meta = new QualityMeta
            {
                Command = context.Config.CommandName,
                GeneratedAt = DateTimeOffset.Now,
                InputFile = context.Config.InputFilePath,
                OutputFile = outputPath,
                ElapsedSeconds = elapsedSeconds
            },
            Counts = new QualityCounts
            {
                SentenceCount = sentences.Count,
                WordCount = words.Count,
                CharacterCount = characters,
                SpeakerCount = DistinctSpeakerCount(sentences),
                DurationSeconds = ComputeDurationSeconds(sentences),
                CharactersPerSecond = ComputeCps(sentences)
            },
            Coverage = new QualityCoverage
            {
                Transcribed = state.IsTranscribed,
                Diarized = state.IsDiarized,
                Split = state.IsSplit,
                Aligned = state.IsAligned,
                Translated = state.IsTranslated,
                TranscribedSentenceCount = state.TranscribeSentences.Count,
                AlignedSentenceCount = state.AlignedSentences.Count,
                TranslatedSentenceCount = state.TranslatedSentences?.Count ?? 0
            },
            Alignment = BuildAlignment(state, sentences),
            Dub = BuildDub(context, sentences),
            Warnings = state.Warnings.ToList(),
            Errors = state.Errors.ToList()
        };

        // The correction command carries its own drift statistics: prefer them directly
        if (state.Report is { AverageDriftMs: > 0 } correction)
        {
            report.Alignment.MeanDriftMs = correction.AverageDriftMs;
            report.Alignment.MaxDriftMs = correction.MaxDriftMs;
            report.Alignment.MapperCoverage = correction.TextCoverage;
        }

        // Line-level quality assessment (CPS / line width / min duration / max duration / overlap / confidence) → Timing + Issues
        var assessment = QualityAssessor.Assess(sentences, new QualityAssessmentOptions(), BuildConfidenceMap(sentences));
        report.Timing = assessment.Timing;
        report.Issues = assessment.Issues;

        // ASR confidence (when the model provides it)
        report.Confidence = BuildConfidence(sentences);
        if (report.Confidence.MeanConfidence is null && sentences.Count > 0)
            report.Warnings.Add("ASR model did not provide per-sentence confidence; LowConfidence analysis is skipped.");

        // Translation QA (TranslationOperator has already written TranslationQa)
        if (state.TranslationQa is not null)
            report.Translation = MapTranslationQa(state.TranslationQa);
        else if (state.IsTranslated && sentences.Count > 0)
            report.Warnings.Add("Translation QA is unavailable (no glossary/length metrics recorded for translated sentences).");

        // TTS / dubbing (only for the dub command)
        if (string.Equals(context.Config.CommandName, "dub", StringComparison.OrdinalIgnoreCase))
            report.Tts = BuildTts(context, sentences);

        return report;
    }

    /// <summary>Per-sentence confidence map (index → 0~1; used only by Assess for low-confidence detection).</summary>
    private static IReadOnlyDictionary<int, double> BuildConfidenceMap(List<Sentence> sentences)
    {
        var map = new Dictionary<int, double>();
        for (var i = 0; i < sentences.Count; i++)
        {
            if (sentences[i].Confidence is { } c)
                map[i] = c;
        }
        return map;
    }

    /// <summary>Aggregates ASR confidence: mean + low-confidence indexes (threshold 0.5).</summary>
    private static QualityConfidence BuildConfidence(List<Sentence> sentences)
    {
        var values = sentences.Select(s => s.Confidence).Where(c => c is not null).Select(c => c!.Value).ToList();
        var confidence = new QualityConfidence
        {
            MeanConfidence = values.Count > 0 ? Math.Round(values.Average(), 3) : null,
            LowConfidenceSentenceIndexes = sentences
                .Select((s, i) => (s, i))
                .Where(x => x.s.Confidence is { } c && c < 0.5)
                .Select(x => x.i)
                .ToList()
        };
        return confidence;
    }

    /// <summary>Maps TranslationQa into the report's translation metrics (back-translation similarity is disabled and always null).</summary>
    private static QualityTranslation MapTranslationQa(TranslationQa qa) => new()
    {
        GlossaryHitRate = qa.GlossaryHitRate,
        GlossaryHits = qa.GlossaryHits,
        GlossaryExpected = qa.GlossaryExpected,
        MeanLengthRatio = qa.MeanLengthRatio,
        LengthDeviation = qa.LengthDeviation,
        CachedSentenceCount = qa.CachedSentenceCount
    };

    /// <summary>TTS metrics for the dub command: alignment error, speech rate, pauses, overlap, ducking (warns when loudness probing is not wired in).</summary>
    private static QualityTts BuildTts(SubtitleWorkflowContext context, List<Sentence> sentences)
    {
        var segments = context.State.DubSegments;
        var succeeded = segments.Where(s => !s.Skipped).ToList();
        var errors = succeeded
            .Where(s => s.TargetDurationSec > 0 && s.AlignedDurationSec > 0)
            .Select(s => Math.Abs(s.TargetDurationSec - s.AlignedDurationSec) * 1000.0)
            .ToList();
        var gaps = new List<double>();
        var negativeGapSeconds = 0.0;
        var ordered = succeeded.OrderBy(s => s.TargetStartMs).ToList();
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var gap = (ordered[i + 1].TargetStartMs - ordered[i].TargetEndMs) / 1000.0;
            gaps.Add(gap);
            if (gap < 0)
                negativeGapSeconds += -gap;
        }

        var tts = new QualityTts
        {
            MeanAlignmentErrorMs = errors.Count > 0 ? Math.Round(errors.Average(), 1) : 0,
            MaxAlignmentErrorMs = errors.Count > 0 ? Math.Round(errors.Max(), 1) : 0,
            MeanSpeechRate = ComputeSpeechRate(sentences),
            MeanPauseSeconds = gaps.Count > 0 ? Math.Round(gaps.Where(g => g > 0).DefaultIfEmpty(0).Average(), 3) : 0,
            MaxPauseSeconds = gaps.Count > 0 ? Math.Round(gaps.Max(), 3) : 0,
            NegativeGapSeconds = Math.Round(negativeGapSeconds, 3),
            DuckingApplied = context.Config.DubDucking && !string.IsNullOrWhiteSpace(context.Config.DubBackgroundPath)
        };
        return tts;
    }

    /// <summary>Average speech rate of TTS-synthesized segments (characters/second; 0 when there are no segments).</summary>
    private static double ComputeSpeechRate(List<Sentence> sentences)
    {
        var segments = sentences
            .Where(s => s.End > s.Start)
            .Select(s => (Chars: s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0, Seconds: (s.End - s.Start) / 1000.0))
            .Where(x => x.Seconds > 0)
            .ToList();
        return segments.Count == 0
            ? 0
            : Math.Round(segments.Average(x => x.Chars / x.Seconds), 2);
    }

    /// <summary>Builds dub-specific metrics (returns null for non-dub commands).</summary>
    private static QualityDub? BuildDub(SubtitleWorkflowContext context, List<Sentence> sentences)
    {
        if (!string.Equals(context.Config.CommandName, "dub", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = context.State.DubSegments;

        var succeeded = segments.Where(s => !s.Skipped).ToList();
        // Dubbing text coverage: share of sentences whose target-language text
        // (the translated track, or the single-track original) is non-empty
        var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText ?? s.Text));
        var deviations = succeeded
            .Where(s => s.TargetDurationSec > 0 && s.AlignedDurationSec > 0)
            .Select(s => Math.Abs(s.TargetDurationSec - s.AlignedDurationSec) * 1000.0)
            .ToList();
        var tempos = succeeded.Where(s => s.AlignmentTempo > 0).Select(s => s.AlignmentTempo).ToList();

        // Clone-consistency heuristic: the closer the synthesized duration is to the target duration,
        // the higher the tempo consistency (0~1); not a real voiceprint similarity
        double consistency = 1.0;
        if (deviations.Count > 0)
        {
            var meanDeviationRatio = deviations.Average() / 1000.0 /
                Math.Max(0.1, succeeded.Where(s => s.TargetDurationSec > 0).Select(s => s.TargetDurationSec).DefaultIfEmpty(1).Average());
            consistency = Math.Round(Math.Clamp(1.0 - meanDeviationRatio, 0.0, 1.0), 3);
        }

        return new QualityDub
        {
            SegmentsTotal = segments.Count,
            SegmentsSucceeded = succeeded.Count,
            SegmentsSkipped = segments.Count - succeeded.Count,
            TranslationCoverage = sentences.Count > 0 ? Math.Round(translatedCount / (double)sentences.Count, 3) : 0,
            MeanAlignmentDeviationMs = deviations.Count > 0 ? Math.Round(deviations.Average(), 1) : 0,
            MaxAlignmentDeviationMs = deviations.Count > 0 ? Math.Round(deviations.Max(), 1) : 0,
            TempoMin = tempos.Count > 0 ? Math.Round(tempos.Min(), 3) : 1,
            TempoMax = tempos.Count > 0 ? Math.Round(tempos.Max(), 3) : 1,
            TempoAverage = tempos.Count > 0 ? Math.Round(tempos.Average(), 3) : 1,
            CloneConsistency = consistency
        };
    }

    /// <summary>Takes the current working set; when empty, falls back to the latest populated stage list (align/split/transcribe).</summary>
    public static List<Sentence> EffectiveSentences(WorkflowState state)
    {
        if (state.CurrentSentences.Count > 0)
            return state.CurrentSentences;

        if (state.AlignedSentences.Count > 0)
            return state.AlignedSentences;

        if (state.DiarizedSentences.Count > 0)
            return state.DiarizedSentences;

        if (state.SplitSentences.Count > 0)
            return state.SplitSentences;

        if (state.TranscribeSentences.Count > 0)
            return state.TranscribeSentences;

        return state.SubtitleSentences.Count > 0 ? state.SubtitleSentences : [];
    }

    private static int DistinctSpeakerCount(List<Sentence> sentences)
    {
        var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in sentences)
        {
            if (!string.IsNullOrWhiteSpace(sentence.Speaker))
                speakers.Add(sentence.Speaker);
            else if (sentence.Words is not null)
            {
                foreach (var word in sentence.Words)
                {
                    if (!string.IsNullOrWhiteSpace(word.Speaker))
                        speakers.Add(word.Speaker);
                }
            }
        }
        return speakers.Count;
    }

    private static double ComputeDurationSeconds(List<Sentence> sentences)
    {
        if (sentences.Count == 0)
            return 0;
        var minStart = sentences.Select(s => s.Start).Min();
        var maxEnd = sentences.Select(s => s.End).Max();
        // Sentence.Start/End are in milliseconds project-wide
        return Math.Max(0, (maxEnd - minStart) / 1000.0);
    }

    private static double ComputeCps(List<Sentence> sentences)
    {
        if (sentences.Count == 0)
            return 0;
        var chars = sentences.Sum(s => s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0);
        var seconds = ComputeDurationSeconds(sentences);
        return seconds > 0 ? Math.Round(chars / seconds, 2) : 0;
    }

    private static QualityAlignment BuildAlignment(WorkflowState state, List<Sentence> sentences)
    {
        var durations = sentences
            .Select(s => s.End - s.Start)   // Start/End are already in milliseconds (removed the redundant ×1000; previously ms was mistaken for s and scaled up 1000×)
            .Where(ms => ms >= 0)
            .ToList();

        var zeroCount = sentences.Count(s => s.End == s.Start && s.Start != 0);

        return new QualityAlignment
        {
            MeanDriftMs = state.Report.AverageDriftMs > 0 ? state.Report.AverageDriftMs : null,
            MaxDriftMs = state.Report.MaxDriftMs > 0 ? state.Report.MaxDriftMs : null,
            MapperCoverage = state.MapperCoverage,
            ZeroDurationSentenceCount = zeroCount,
            MinSentenceDurationMs = durations.Count > 0 ? durations.Min() : 0,
            MaxSentenceDurationMs = durations.Count > 0 ? durations.Max() : 0
        };
    }
}
