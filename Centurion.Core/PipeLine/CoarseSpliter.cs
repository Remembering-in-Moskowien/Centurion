using Centurion.Core.Abstractions;
using Centurion.Core.Models;

namespace Centurion.Core.PipeLine;

/// <summary>
/// Coarse sentence splitting based on time gaps (>3000ms) and sentence-final punctuation (. ? !).
/// This produces initial segments for speaker diarization.
/// </summary>
public class CoarseSplitOperator : PipelineOperatorBase
{
    public override string Name => "Coarse Sentence Splitting";

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // Checkpoint: if already split coarsely, skip
        if (context.State.CoarseSentences != null && context.State.CoarseSentences.Count > 0)
        {
            LogInfo("Coarse sentences already exist, skipping.");
            return;
        }

        var source = context.State.WhisperSentences;
        if (source == null || source.Count == 0)
        {
            LogWarning("No Whisper sentences to split coarsely.");
            context.State.CoarseSentences = new List<Sentence>();
            return;
        }

        // We expect Whisper to produce a single sentence containing all words.
        // If multiple, we process each.
        var allCoarse = new List<Sentence>();
        foreach (var sentence in source)
        {
            if (sentence.Words == null || sentence.Words.Count == 0)
                continue;

            var coarse = SplitCoarse(sentence.Words);
            allCoarse.AddRange(coarse);
        }

        context.State.CoarseSentences = allCoarse;
        LogInfo($"Generated {allCoarse.Count} coarse sentences.");
        await Task.CompletedTask;
    }

    private List<Sentence> SplitCoarse(List<Word> words)
    {
        if (words == null || words.Count == 0)
            return new List<Sentence>();

        var segments = new List<List<Word>>();
        var current = new List<Word>();

        foreach (var w in words)
        {
            if (current.Count == 0)
            {
                current.Add(w);
                continue;
            }

            var shouldSplit = false;

            // 1. Time gap > 3000ms
            var gapMs = w.Start - current.Last().End;
            if (gapMs > 3000)
                shouldSplit = true;

            // 2. Previous word ends with . ? !
            if (!shouldSplit)
            {
                var lastText = current.Last().Text.Trim();
                if (lastText.Length > 0 && ".?!".Contains(lastText[^1]))
                    shouldSplit = true;
            }

            // 3. Capitalised word (for languages with case)
            if (!shouldSplit)
            {
                var currentWord = w.Text.Trim();
                if (currentWord.Length > 1 &&
                    char.IsUpper(currentWord[0]) &&
                    !IsAllUpper(currentWord) &&
                    !IsAbbreviation(currentWord))
                    shouldSplit = true;
            }

            if (shouldSplit)
            {
                segments.Add(current);
                current = new List<Word> { w };
            }
            else
            {
                current.Add(w);
            }
        }

        if (current.Count > 0)
            segments.Add(current);

        // Build Sentence objects
        var result = new List<Sentence>();
        foreach (var seg in segments)
        {
            var text = string.Join(" ", seg.Select(w => w.Text));
            result.Add(new Sentence
            {
                Text = text,
                Start = seg.First().Start,
                End = seg.Last().End,
                Words = seg
            });
        }

        return result;
    }

    private static bool IsAllUpper(string text)
    {
        return !string.IsNullOrEmpty(text) && text.All(c => !char.IsLetter(c) || char.IsUpper(c));
    }

    private static bool IsAbbreviation(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string[] common = ["Mr.", "Mrs.", "Ms.", "Dr.", "e.g.", "i.e.", "vs.", "etc.", "U.S."];
        if (common.Contains(text)) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Z](?:\.[A-Z])+\.?$");
    }
}