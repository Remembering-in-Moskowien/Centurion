using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Text;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// Shared abstract base class for rule-based splitting strategies (common logic for the aggressive and passive tiers).
/// Both tiers force sentence-ending and clause punctuation (Latin and CJK sets) as the highest-priority breaks,
/// and support speaker awareness (forced breaks at speaker switches) and CJK spacing-less word joining.
/// They differ only in break candidates: the aggressive tier also treats significant inter-word pauses as hard breaks
/// (suited to short, dense dialogue), while the passive tier honors only punctuation and uses global DP for optimal breaks (suited to monologue).
/// </summary>
public abstract class RuleBasedSplitStrategyBase : BaseSplitStrategy
{
    /// <summary>All punctuation that can act as a break (Latin scripts plus common CJK, South Asian, and Arabic punctuation).</summary>
    protected static readonly HashSet<char> BreakPunctuation =
        ['.', '!', '?', ',', ';', ':', .. LanguageSupport.CjkBreakPunctuation];

    /// <summary>Fallback label used when diarization has no match; not treated as a real speaker.</summary>
    private const string UnknownSpeaker = "SPEAKER_00";

    /// <summary>
    /// Speaker-aware splitting: first force-group the word stream by speaker switches, then call <see cref="SplitGroupByPunctuation"/> on each group.
    /// </summary>
    /// <param name="words">The word stream to split.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words == null || words.Count == 0)
            return [];

        // 1. Order the words by time
        var wordList = words.OrderBy(w => w.Start).ToList();

        // 2. Speaker awareness: force a break at each speaker switch between adjacent words (consecutive words of the same speaker form a group)
        var groups = SplitBySpeaker(wordList);

        var sentences = new List<Sentence>();
        foreach (var group in groups)
            sentences.AddRange(await SplitGroupByPunctuation(group, options));

        return sentences;
    }

    /// <summary>
    /// The concrete break strategy implemented by subclasses (aggressive tier = punctuation + pause hard breaks; passive tier = punctuation + global DP).
    /// </summary>
    /// <param name="wordList">The word stream of a single speaker, ordered by time.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    protected abstract Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options);

    /// <summary>
    /// Force-groups the word stream by speaker switches: a boundary is drawn when two adjacent words both carry valid speaker labels and the labels differ.
    /// Words with no label (fallback label / empty) neither trigger a split nor break continuity within a group.
    /// </summary>
    /// <param name="words">The word stream ordered by time.</param>
    /// <returns>The list of word streams grouped by speaker switch.</returns>
    private static List<List<Word>> SplitBySpeaker(List<Word> words)
    {
        var groups = new List<List<Word>>();
        var current = new List<Word> { words[0] };

        for (var i = 1; i < words.Count; i++)
        {
            var prevSpeaker = words[i - 1].Speaker;
            var curSpeaker = words[i].Speaker;
            if (IsRealSpeaker(prevSpeaker) && IsRealSpeaker(curSpeaker) &&
                !string.Equals(prevSpeaker, curSpeaker, StringComparison.Ordinal))
            {
                groups.Add(current);
                current = [words[i]];
            }
            else
            {
                current.Add(words[i]);
            }
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups;
    }

    /// <summary>Whether a speaker label is real: non-empty and not the diarization-miss fallback label.</summary>
    private static bool IsRealSpeaker(string? speaker) =>
        !string.IsNullOrWhiteSpace(speaker) &&
        !string.Equals(speaker, UnknownSpeaker, StringComparison.Ordinal);

    /// <summary>Whether the word text ends with a break/clause punctuation (true sentence boundaries lie after such words).</summary>
    protected static bool EndsWithBreakPunctuation(string? text) =>
        !string.IsNullOrEmpty(text) && BreakPunctuation.Contains(text[^1]);

    /// <summary>Counts the display length of a word segment (mixed-aware: CJK-like words join directly, others count one space between).</summary>
    protected static int SliceCharCount(List<Word> slice) =>
        LanguageSupport.JoinMixed(slice.Select(w => w.Text)).Length;

    /// <summary>
    /// Precomputes whether a space is inserted between adjacent words (no space between CJK-like words, otherwise one space),
    /// so the DP can incrementally compute clause char counts in O(1).
    /// </summary>
    /// <param name="texts">The word texts ordered by time.</param>
    /// <returns>sepBefore[i] = number of spaces before word i (0 when i=0).</returns>
    protected static int[] ComputeSeparatorBefore(List<string> texts)
    {
        var sepBefore = new int[texts.Count];
        for (var i = 1; i < texts.Count; i++)
        {
            sepBefore[i] = LanguageSupport.IsCjkToken(texts[i - 1]) && LanguageSupport.IsCjkToken(texts[i])
                ? 0
                : 1;
        }
        return sepBefore;
    }

    /// <summary>Builds a sentence from a word segment (text joined with mixed-aware logic, timing taken from the first and last words).</summary>
    protected static Sentence BuildSentence(List<Word> slice, SplitOptions options)
    {
        var text = LanguageSupport.JoinMixed(slice.Select(w => w.Text));
        return new Sentence
        {
            Text = SubTools.NormalizeSpaces(text),
            Start = slice.First().Start,
            End = slice.Last().End,
            Words = slice
        };
    }
}
