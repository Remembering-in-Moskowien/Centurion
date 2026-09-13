using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Models;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 校正已有字幕的时间轴。
///
/// 断句契约：
///   • 输入必须是从原字幕解析出的多个 Sentence（每个 Dialogue 行一个）；
///   • 输出保持与输入相同的句子数量与顺序，不合并、不拆分；
///   • 输入源优先使用 <c>SubtitleSentences</c>，回退到 <c>CurrentSentences</c>。
///
/// 算法：与 <see cref="ScriptTimelineMapperOp"/> 同构的全局词级 NW 对齐。
///   • 不做窗口锚定——放弃原字幕时间戳作为对齐依据；
///   • 不做空隙均分填充——未命中的句子保留原时间戳；
///   • 未匹配的转录词按连续段分组，≥3 个视为 [SPONT]；
///   • 匹配到的句子时间戳取首尾非零词；记录 DriftMs。
///
/// NW 打分与后处理由基类 <see cref="TimelineAlignmentOpBase{TSelf}"/> 提供。
/// </summary>
public sealed class SubtitleTimelineCorrectorOp : TimelineAlignmentOpBase<SubtitleTimelineCorrectorOp>
{
    /// <summary>连续多少个未匹配音频词才合并为 [SPONT]。</summary>
    private const int MinConsecutiveSpont = 3;

    private readonly ILogger<SubtitleTimelineCorrectorOp> _logger;

    public SubtitleTimelineCorrectorOp(ILogger<SubtitleTimelineCorrectorOp> logger) : base(logger)
    {
        _logger = logger;
    }

    public override string Name => "Subtitle Timeline Correction";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var subtitles = SelectSubtitleSource(context);
        var audioWords = context.State.TranscribeSentences
            .SelectMany(s => s.Words)
            .Where(w => w.End > w.Start)
            .OrderBy(w => w.Start)
            .ToList();

        if (subtitles.Count == 0)
            throw new InvalidOperationException("No subtitle sentences are available for timeline correction.");

        _logger.LogInformation(
            "Timeline correction input: {SubtitleCount} subtitle sentences, {AudioWordCount} audio words.",
            subtitles.Count, audioWords.Count);

        if (audioWords.Count == 0)
        {
            LogWarning("Timeline correction skipped because transcription contains no words.");
            var copy = subtitles.Select(CloneSentence).ToList();
            context.State.CorrectedSentences = copy;
            context.State.CurrentSentences = copy;
            return Task.CompletedTask;
        }

        var metadata = CorrectionMetadata.Get(context.State);

        // ==== 1. 展平字幕 token ====
        var flatScriptNorm = new List<string>();
        var flatOwner = new List<int>();
        var perSentenceTokenCount = new List<int>(subtitles.Count);

        for (var i = 0; i < subtitles.Count; i++)
        {
            var beforeCount = flatScriptNorm.Count;
            foreach (var token in ExtractTokens(GetSentenceText(subtitles[i])))
            {
                var norm = NormalizeWord(token);
                if (string.IsNullOrEmpty(norm)) continue;
                flatScriptNorm.Add(norm);
                flatOwner.Add(i);
            }
            perSentenceTokenCount.Add(flatScriptNorm.Count - beforeCount);
        }

        // ==== 2. 全局 NW ====
        var audioNorm = audioWords.Select(w => NormalizeWord(w.Text)).ToArray();
        var flatMapping = AlignByWordLevelNw(flatScriptNorm, audioNorm, cancellationToken);

        // ==== 3. 聚合：每句匹配到的转录词索引（升序去重）====
        var sentenceMatchedWords = new List<List<int>>(subtitles.Count);
        for (var i = 0; i < subtitles.Count; i++)
            sentenceMatchedWords.Add(new List<int>());

        for (var i = 0; i < flatMapping.Length; i++)
        {
            var tj = flatMapping[i];
            if (tj >= 0)
                sentenceMatchedWords[flatOwner[i]].Add(tj);
        }

        for (var i = 0; i < sentenceMatchedWords.Count; i++)
        {
            sentenceMatchedWords[i] = sentenceMatchedWords[i]
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        // ==== 4. 未匹配转录词 → [SPONT] 候选 ====
        var matchedIndices = CollectMatchedTranscriptIndices(flatMapping);
        var unmatchedSegments = SplitUnmatchedSegments(audioWords.Count, matchedIndices);

        // ==== 5. 逐句输出 ====
        var corrected = new List<Sentence>(subtitles.Count);
        for (var i = 0; i < subtitles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subtitle = subtitles[i];
            var result = CloneSentence(subtitle);

            if (perSentenceTokenCount[i] == 0)
            {
                SetAction(metadata, result, "kept");
                corrected.Add(result);
                continue;
            }

            if (sentenceMatchedWords[i].Count == 0)
            {
                SetAction(metadata, result, "unmatched");
                corrected.Add(result);
                continue;
            }

            result.Words = sentenceMatchedWords[i]
                .Select(idx => CloneWord(audioWords[idx]))
                .ToList();

            var timedWords = result.Words.Where(w => w.End > w.Start).ToList();
            if (timedWords.Count == 0)
            {
                SetAction(metadata, result, "unmatched");
                corrected.Add(result);
                continue;
            }

            result.Start = timedWords[0].Start;
            result.End = timedWords[^1].End;

            var drift = result.Start - subtitle.Start;
            SetMetadata(metadata, result, CorrectKeys.DriftMs, drift);
            var isRetimed = Math.Abs(drift) > context.Config.MaxDriftMs;
            SetAction(metadata, result, isRetimed ? "retimed" : "kept");

            corrected.Add(result);
        }

        // ==== 6. 插入 [SPONT]（连续 ≥3 个未匹配音频词）====
        foreach (var seg in unmatchedSegments)
        {
            if (seg.Count < MinConsecutiveSpont) continue;
            var segWords = seg.Select(idx => audioWords[idx]).ToList();
            InsertSpont(corrected, metadata, segWords);
        }

        // ==== 7. 排序与状态更新 ====
        context.State.CorrectedSentences = corrected
            .OrderBy(s => s.Start)
            .ToList();
        context.State.CurrentSentences = context.State.CorrectedSentences;

        // ==== 8. 终算 Report ====
        ComputeReport(
            context.State.Report,
            subtitles.Count,
            context.State.CorrectedSentences,
            metadata);

        _logger.LogInformation(
            "Timeline correction done: {OutputCount} output sentences, " +
            "{Shifted} shifted, {Unmatched} unmatched, avg drift {Drift:F0}ms.",
            context.State.CorrectedSentences.Count,
            context.State.Report.TimelineShifted,
            context.State.Report.Unmatched,
            context.State.Report.AverageDriftMs);

        OnProgress(100, "Timeline correction completed");
        return Task.CompletedTask;
    }

    private static IReadOnlyList<Sentence> SelectSubtitleSource(SubtitleWorkflowContext context)
    {
        if (context.State.SubtitleSentences is { Count: > 0 })
            return context.State.SubtitleSentences;
        return context.State.CurrentSentences;
    }

    // ================== [SPONT] ==================

    private static void InsertSpont(
        List<Sentence> output,
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        IReadOnlyList<Word> segment)
    {
        if (segment.Count < MinConsecutiveSpont) return;

        var words = segment.Select(word =>
        {
            var clone = CloneWord(word);
            clone.Status = MappingStatus.AudioExtra;
            return clone;
        }).ToList();

        var text = "[SPONT] " + string.Join(" ", words.Select(word => word.Text));
        var spont = new Sentence
        {
            Text = text,
            CleanedText = text,
            Start = words[0].Start,
            End = words[^1].End,
            Words = words
        };

        metadata[spont] = new Dictionary<string, object>
        {
            [CorrectKeys.Origin] = "spont",
            [CorrectKeys.Action] = "inserted"
        };

        output.Add(spont);
    }

    // ================== Report 终算 ==================

    private static void ComputeReport(
        CorrectionReport report,
        int totalSentences,
        IReadOnlyList<Sentence> corrected,
        Dictionary<Sentence, Dictionary<string, object>> metadata)
    {
        report.TotalSentences = totalSentences;
        report.TimelineShifted = 0;
        report.Unmatched = 0;
        report.Removed = 0;

        var drifts = new List<double>();
        var nonSpontCount = 0;

        foreach (var sentence in corrected)
        {
            var action = GetMetadataString(metadata, sentence, CorrectKeys.Action);
            var origin = GetMetadataString(metadata, sentence, CorrectKeys.Origin);

            switch (action)
            {
                case "retimed":
                    report.TimelineShifted++;
                    break;
                case "unmatched":
                    report.Unmatched++;
                    break;
            }

            if (sentence.SkipRender)
                report.Removed++;

            if (metadata.TryGetValue(sentence, out var values) &&
                values.TryGetValue(CorrectKeys.DriftMs, out var driftObj) &&
                driftObj is double drift)
            {
                drifts.Add(Math.Abs(drift));
            }

            if (origin != "spont")
                nonSpontCount++;
        }

        report.AverageDriftMs = drifts.Count > 0 ? drifts.Average() : 0;
        report.TextCoverage = totalSentences > 0
            ? (double)nonSpontCount / totalSentences
            : 0;
    }

    private static string? GetMetadataString(
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        string key)
    {
        if (!metadata.TryGetValue(sentence, out var values)) return null;
        return values.TryGetValue(key, out var value) ? value as string : null;
    }

    // ================== 元数据与克隆 ==================

    private static Sentence CloneSentence(Sentence source) => new()
    {
        Text = source.Text,
        CleanedText = source.CleanedText,
        Start = source.Start,
        End = source.End,
        SkipRender = source.SkipRender,
        Words = source.Words.Select(CloneWord).ToList()
    };

    private static Word CloneWord(Word source) => new()
    {
        Text = source.Text,
        Start = source.Start,
        End = source.End,
        Speaker = source.Speaker,
        PosTag = source.PosTag,
        Status = source.Status
    };

    private static void SetAction(
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        string action) =>
        SetMetadata(metadata, sentence, CorrectKeys.Action, action);

    private static void SetMetadata(
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        string key,
        object value)
    {
        if (!metadata.TryGetValue(sentence, out var values))
            metadata[sentence] = values = new Dictionary<string, object>();
        values[key] = value;
    }
}