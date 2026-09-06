using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public sealed class ScriptTimelineMapperOp(ILogger<ScriptTimelineMapperOp> logger) : PipelineOperatorBase
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uh", "um", "you know", "like", "呃", "啊", "那个"
    };

    private static readonly Regex TokenPattern = new(@"[\p{L}\p{N}]+|[\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly Regex PunctuationPattern = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);

    public override string Name => "Script Timeline Mapping";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var scripts = context.State.ScriptSentences;
        var transcript = context.State.TranscribeSentences.FirstOrDefault();
        if (scripts.Count == 0 || transcript?.Words.Count is not > 0)
        {
            context.State.CoarseSentences = scripts;
            context.State.MapperCoverage = 0;
            logger.LogWarning("Script mapping skipped because script or transcription words are empty.");
            return Task.CompletedTask;
        }

        var audioWords = transcript.Words;
        var audioNormalized = audioWords.Select(word => Normalize(word.Text, context.Config)).ToArray();
        var usedAudio = new HashSet<int>();
        var matchedCount = 0;
        var totalScriptWords = 0;
        var cursor = 0;

        foreach (var sentence in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sentence.Words.Clear();
            sentence.Extensions.Clear();

            var scriptTokens = TokenPattern.Matches(sentence.Text).Select(match => match.Value).ToList();
            totalScriptWords += scriptTokens.Count;
            var matchedIndexes = new List<int>();

            foreach (var token in scriptTokens)
            {
                var normalized = Normalize(token, context.Config);
                var bestIndex = -1;
                var bestScore = 0.0;
                var previousEnd = matchedIndexes.Count == 0 ? double.NaN : audioWords[matchedIndexes[^1]].End;

                for (var index = cursor; index < audioWords.Count; index++)
                {
                    if (usedAudio.Contains(index))
                        continue;
                    if (!double.IsNaN(previousEnd) && audioWords[index].Start - previousEnd > 2000)
                        break;
                    if (string.IsNullOrWhiteSpace(audioNormalized[index]))
                        continue;

                    var score = Similarity(normalized, audioNormalized[index]);
                    if (StopWords.Contains(audioNormalized[index]))
                        score = 0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = index;
                    }
                }

                var word = new Word
                {
                    Text = token,
                    Start = 0,
                    End = 0,
                    Speaker = "UNKNOWN",
                    Status = MappingStatus.ScriptMissing
                };

                if (bestIndex >= 0 && bestScore > 0.85)
                {
                    var audioWord = audioWords[bestIndex];
                    word.Start = audioWord.Start;
                    word.End = audioWord.End;
                    word.Status = MappingStatus.Matched;
                    usedAudio.Add(bestIndex);
                    matchedIndexes.Add(bestIndex);
                    cursor = bestIndex + 1;
                    matchedCount++;
                }

                sentence.Words.Add(word);
            }

            AssignMissingTimes(sentence);
            sentence.Start = sentence.Words.Count == 0 ? 0 : sentence.Words.Min(word => word.Start);
            sentence.End = sentence.Words.Count == 0 ? sentence.Start : sentence.Words.Max(word => word.End);

            var sentenceStart = sentence.Start - 2000;
            var sentenceEnd = sentence.End + 2000;
            foreach (var index in Enumerable.Range(0, audioWords.Count))
            {
                if (!usedAudio.Contains(index) && audioWords[index].Start >= sentenceStart && audioWords[index].Start <= sentenceEnd)
                {
                    var extra = CloneWord(audioWords[index], MappingStatus.AudioExtra, "[SPONT]");
                    sentence.Extensions.Add(extra);
                    sentence.Words.Add(extra);
                }
            }
            sentence.Words = [.. sentence.Words.OrderBy(word => word.Start)];
        }

        context.State.MapperCoverage = totalScriptWords == 0 ? 0 : (double)matchedCount / totalScriptWords;
        context.State.CoarseSentences = scripts;
        if (context.State.MapperCoverage < context.Config.CoverageThreshold)
        {
            var warning = $"Script mapping coverage {context.State.MapperCoverage:P1} is below threshold {context.Config.CoverageThreshold:P1}.";
            context.State.Warnings.Add(warning);
            logger.LogWarning(warning);
        }
        else
        {
            logger.LogInformation("Script mapping coverage: {Coverage:P1}.", context.State.MapperCoverage);
        }

        OnProgress(100, $"Mapped script with {context.State.MapperCoverage:P1} coverage.");
        return Task.CompletedTask;
    }

    private static string Normalize(string text, WorkflowConfig config)
    {
        var normalized = TextPreprocessingOp.CleanText(text, config);
        return PunctuationPattern.Replace(normalized, "").Trim();
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0;
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
            return 1;

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }

        return 1.0 - (double)previous[^1] / Math.Max(left.Length, right.Length);
    }

    private static void AssignMissingTimes(Sentence sentence)
    {
        var matched = sentence.Words.Where(word => word.Status == MappingStatus.Matched).ToList();
        if (matched.Count == 0)
            return;

        for (var index = 0; index < sentence.Words.Count; index++)
        {
            if (sentence.Words[index].Status == MappingStatus.Matched)
                continue;
            var previous = sentence.Words.Take(index).LastOrDefault(word => word.Status == MappingStatus.Matched);
            var next = sentence.Words.Skip(index + 1).FirstOrDefault(word => word.Status == MappingStatus.Matched);
            var start = previous?.End ?? matched[0].Start;
            var end = next?.Start ?? matched[^1].End;
            if (end < start)
                end = start;
            sentence.Words[index].Start = start;
            sentence.Words[index].End = end;
        }
    }

    private static Word CloneWord(Word word, MappingStatus status, string prefix) => new()
    {
        Text = $"{prefix}{word.Text}",
        Start = word.Start,
        End = word.End,
        Speaker = word.Speaker,
        Status = status
    };
}