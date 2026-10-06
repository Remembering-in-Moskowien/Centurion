using System.Runtime.InteropServices;
using System.Text;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Infrastructure;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Text;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// SaT (segment-any-text) sentence-segmentation strategy: a local token-classification model
/// (XLM-R based, exported to ONNX) predicts, for every token, the probability that the next token
/// starts a new sentence. The model runs on GPU when available (DirectML execution provider) and
/// falls back to CPU automatically. No Python runtime is required.
///
/// The strategy downloads the model on first use, encodes the word stream with the XLM-R BPE
/// tokenizer, runs the ONNX graph in sliding windows (overlapping windows are averaged), converts
/// token-level boundary probabilities to word-boundary split points, and applies length/duration
/// caps as a safety net. On any model failure it degrades to a simple length-based split instead
/// of failing the whole pipeline.
/// </summary>
public sealed class SaTSplitStrategy : BaseSplitStrategy
{
    // Model maximum 514 positions (512 including the two special tokens).
    private const int MaxWindowTokens = 512;
    private const int WindowStrideTokens = 256;

    private readonly ModelManager _manager;
    private readonly double _threshold;
    private readonly ILogger<SaTSplitStrategy>? _logger;
    private readonly Lock _sessionLock = new();
    private InferenceSession? _session;

    /// <summary>Creates a SaT strategy backed by the given model manager.</summary>
    /// <param name="manager">Model manager for the SaT model (auto-installs on first use).</param>
    /// <param name="threshold">Boundary probability threshold in (0, 1); 0.5 is the wtpsplit default.</param>
    /// <param name="logger">Optional logger.</param>
    public SaTSplitStrategy(ModelManager manager, double threshold = 0.5, ILogger<SaTSplitStrategy>? logger = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _threshold = threshold is > 0 and < 1 ? threshold : 0.5;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words == null || words.Count == 0) return [];
        var ordered = words.OrderBy(w => w.Start).ToList();

        try
        {
            // Ensure the model is installed (auto-download on first use), then run SaT inference.
            await _manager.EnsureInstalledAsync();
            var sentences = SplitWithModel(ordered, options);
            _logger?.LogInformation(
                "SaT split '{Model}' into {Count} sentences (threshold {Threshold:0.00}).",
                _manager.TargetMeta != null ? Path.GetFileName(_manager.ModelFilePath) : "sat", sentences.Count, _threshold);
            return sentences;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SaT splitting failed; falling back to length-based splitting.");
            return FallbackSplit(ordered, options);
        }
    }

    // ---------- Model path ----------

    private InferenceSession GetSession()
    {
        if (_session is not null) return _session;
        lock (_sessionLock)
        {
            if (_session is not null) return _session;
            var modelPath = Path.Combine(_manager.ModelFilePath, "model.onnx");
            if (!File.Exists(modelPath))
                throw new InvalidOperationException($"SaT model file not found: '{modelPath}'.");
            _session = new InferenceSession(modelPath, OnnxSessionFactory.CreateSessionOptions());
            return _session;
        }
    }

    // ---------- Inference ----------

    private List<Sentence> SplitWithModel(List<Word> words, SplitOptions options)
    {
        // Normalized joined text (NFKC per word, single spaces) with per-word char starts for mapping.
        var normText = new StringBuilder();
        var wordStarts = new List<int>();
        var normWords = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var w = NormalizeWord(words[i].Text);
            if (w.Length == 0) continue;
            if (normText.Length > 0) normText.Append(' ');
            wordStarts.Add(normText.Length);
            normWords.Add(w);
            normText.Append(w);
        }
        if (normWords.Count == 0) return [];

        var tokenizer = XlmRBpeTokenizer.Load(_manager.ModelFilePath);
        var (ids, pieces, offsets) = tokenizer.Encode(normText.ToString());
        var contentCount = ids.Length - 2; // without <s> and </s>
        if (contentCount <= 1)
            return [BuildSentence(words, options)];

        var probs = ComputeBoundaryProbabilities(ids, contentCount);

        // Split points = word indices (the word that starts the next sentence).
        // probs[j] is the boundary probability after content token j (the SaT logits are per token,
        // with a high value marking a sentence boundary right after that token).
        var splitWords = new List<int>();
        for (var j = 0; j < contentCount - 1; j++) // the last token cannot start a new sentence
        {
            if (probs[j] <= _threshold) continue;
            var nextOffset = offsets[j + 1];
            var wordIndex = TokenToWordIndex(nextOffset, wordStarts, normWords.Count);
            if (wordIndex > 0 && wordIndex < normWords.Count && !splitWords.Contains(wordIndex))
                splitWords.Add(wordIndex);
        }
        splitWords.Sort();

        // Map normalized words back to original word indices.
        var normToOriginal = new List<int>();
        var origIndex = 0;
        for (var i = 0; i < normWords.Count; i++)
        {
            while (origIndex < words.Count && NormalizeWord(words[origIndex].Text).Length == 0) origIndex++;
            normToOriginal.Add(origIndex);
            origIndex++;
        }

        var groups = BuildGroups(splitWords, normWords.Count);
        var result = new List<Sentence>();
        foreach (var (startNorm, endNormExclusive) in groups)
        {
            var start = normToOriginal[startNorm];
            var end = normToOriginal[endNormExclusive - 1];
            result.Add(BuildSentence(words[start..(end + 1)], options));
        }

        // Safety net: enforce MaxLength / MaxDuration caps on model output.
        var capped = new List<Sentence>();
        foreach (var sentence in result)
            capped.AddRange(SplitOversized(sentence, options));
        return capped.Count > 0 ? capped : result;
    }

    private static string NormalizeWord(string text)
    {
        var nfkc = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var r in nfkc.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(r) || r.Value == '\u200b' || r.Value == '\ufeff' || Rune.IsControl(r)) continue;
            sb.Append(r.ToString());
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Computes per-content-token boundary probabilities by running sliding windows over the input,
    /// averaging probabilities in overlapping regions.
    /// </summary>
    private float[] ComputeBoundaryProbabilities(int[] ids, int contentCount)
    {
        var session = GetSession();
        var sums = new double[contentCount];
        var counts = new int[contentCount];

        for (var start = 0; start < contentCount; start += WindowStrideTokens)
        {
            var length = Math.Min(MaxWindowTokens - 2, contentCount - start);
            if (length <= 0) break;
            var windowIds = new int[length + 2];
            windowIds[0] = ids[0];
            windowIds[^1] = ids[^1];
            Array.Copy(ids, start + 1, windowIds, 1, length);

            var windowProbs = RunWindow(session, windowIds);
            for (var i = 0; i < windowProbs.Length; i++)
            {
                sums[start + i] += windowProbs[i];
                counts[start + i]++;
            }
        }

        var result = new float[contentCount];
        for (var i = 0; i < contentCount; i++)
            result[i] = counts[i] > 0 ? (float)(sums[i] / counts[i]) : 0f;
        return result;
    }

    private static float[] RunWindow(InferenceSession session, int[] windowIds)
    {
        var seq = windowIds.Length;
        var maskBytes = new byte[seq * 2];
        for (var i = 0; i < seq; i++)
        {
            maskBytes[i * 2] = 0x00;
            maskBytes[i * 2 + 1] = 0x3C; // Half(1.0f) little-endian
        }

        var inputIds = new long[seq];
        for (var i = 0; i < seq; i++) inputIds[i] = windowIds[i];

        var maskHandle = GCHandle.Alloc(maskBytes, GCHandleType.Pinned);
        try
        {
            using var inputOrt = OrtValue.CreateTensorValueFromMemory(inputIds, new long[] { 1, seq });
            using var maskOrt = OrtValue.CreateTensorValueWithData(
                OrtMemoryInfo.DefaultInstance, TensorElementType.Float16, new long[] { 1, seq },
                maskHandle.AddrOfPinnedObject(), maskBytes.Length);
            using var results = session.Run(
                new RunOptions(),
                new Dictionary<string, OrtValue> { ["input_ids"] = inputOrt, ["attention_mask"] = maskOrt },
                new[] { "logits" });
            var logits = results[0].GetTensorDataAsSpan<Microsoft.ML.OnnxRuntime.Float16>().ToArray();
            var probs = new float[seq - 2];
            for (var i = 0; i < probs.Length; i++)
            {
                var logit = (float)logits[i + 1];
                probs[i] = (float)(1.0 / (1.0 + Math.Exp(-logit)));
            }
            return probs;
        }
        finally
        {
            maskHandle.Free();
        }
    }

    /// <summary>Maps a character offset to the word that contains it (the last word start &lt;= offset).</summary>
    private static int TokenToWordIndex(int offset, List<int> wordStarts, int wordCount)
    {
        var lo = 0;
        var hi = wordCount;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (wordStarts[mid] <= offset) lo = mid + 1;
            else hi = mid;
        }
        return Math.Min(lo, wordCount) - 1; // word containing offset (offset >= wordStarts[0] = 0)
    }

    private static List<(int Start, int EndExclusive)> BuildGroups(List<int> splitWords, int wordCount)
    {
        var groups = new List<(int, int)>();
        var prev = 0;
        foreach (var split in splitWords)
        {
            if (split > prev) groups.Add((prev, split));
            prev = split;
        }
        if (prev < wordCount) groups.Add((prev, wordCount));
        if (groups.Count == 0) groups.Add((0, wordCount));
        return groups;
    }

    // ---------- Safety nets ----------

    private static Sentence BuildSentence(List<Word> slice, SplitOptions options)
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

    private static List<Sentence> SplitOversized(Sentence sentence, SplitOptions options)
    {
        if (sentence.Words is not { Count: > 1 }) return [sentence];
        var lengthOk = sentence.Text.Length <= Math.Max(1, options.MaxLength);
        var durationOk = sentence.End - sentence.Start <= Math.Max(0.1, options.MaxDuration);
        if (lengthOk && durationOk) return [sentence];

        var words = sentence.Words;
        var target = Math.Max(1, options.MaxLength);
        // Estimate words per chunk from the current length ratio, clamped to sensible bounds.
        var wordsPerChunk = Math.Clamp(
            (int)Math.Ceiling((double)words.Count * target / Math.Max(1, sentence.Text.Length)),
            1, Math.Max(1, words.Count));
        var chunks = (int)Math.Ceiling((double)words.Count / wordsPerChunk);
        if (chunks < 2) return [sentence];

        var result = new List<Sentence>();
        for (var i = 0; i < chunks; i++)
        {
            var start = i * wordsPerChunk;
            var end = Math.Min(words.Count, start + wordsPerChunk);
            if (start >= end) break;
            result.Add(BuildSentence(words[start..end], options));
        }
        return result;
    }

    private static List<Sentence> FallbackSplit(List<Word> ordered, SplitOptions options)
    {
        var chunkSize = Math.Max(1, options.MaxWordsPerLine);
        var result = new List<Sentence>();
        for (var i = 0; i < ordered.Count; i += chunkSize)
        {
            var slice = ordered.Skip(i).Take(chunkSize).ToList();
            if (slice.Count == 0) break;
            result.Add(BuildSentence(slice, options));
        }
        return result;
    }
}
