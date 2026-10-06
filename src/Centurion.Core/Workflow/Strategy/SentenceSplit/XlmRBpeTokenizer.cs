using System.Collections.Concurrent;
using System.Text.Json;
using SIL.Machine.Tokenization.SentencePiece;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// XLM-R SentencePiece BPE tokenizer implemented in pure C# (no Python).
///
/// The XLM-R family (including wtpsplit's SaT sentence-segmentation models) uses a SentencePiece BPE
/// model whose merges are executed by the standard SentencePiece algorithm; the final token ids follow
/// the transformers vocabulary order ({"&lt;s&gt;": 0, "&lt;pad&gt;": 1, "&lt;/s&gt;": 2, "&lt;unk&gt;": 3,
/// then the normal pieces) as serialized in <c>tokenizer.json</c>. This class combines both sources:
/// subword segmentation comes from the SentencePiece model (SIL.Machine, a pure-C# SentencePiece
/// implementation whose output matches the reference tokenizer, verified on real XLM-R fixtures), the
/// id mapping from tokenizer.json.
/// </summary>
public sealed class XlmRBpeTokenizer
{
    private readonly SentencePieceTokenizer _sp;
    private readonly Dictionary<string, int> _vocab;      // piece -> transformers id
    private readonly int _sId;
    private readonly int _eosId;
    private readonly int _unkId;

    /// <summary>Shared tokenizer instances keyed by model folder (parse is ~10 MB of JSON).</summary>
    private static readonly ConcurrentDictionary<string, Lazy<XlmRBpeTokenizer>> Cache = new();

    /// <summary>Gets a cached tokenizer for the given model folder.</summary>
    /// <param name="modelFolder">Directory containing sentencepiece.bpe.model and tokenizer.json.</param>
    public static XlmRBpeTokenizer Load(string modelFolder) =>
        Cache.GetOrAdd(modelFolder, static folder =>
            new Lazy<XlmRBpeTokenizer>(() => new XlmRBpeTokenizer(folder))).Value;

    private XlmRBpeTokenizer(string modelFolder)
    {
        var spmPath = Path.Combine(modelFolder, "sentencepiece.bpe.model");
        var tokenizerPath = Path.Combine(modelFolder, "tokenizer.json");
        if (!File.Exists(spmPath) || !File.Exists(tokenizerPath))
            throw new InvalidOperationException(
                $"SaT tokenizer files are missing in '{modelFolder}' (expected sentencepiece.bpe.model and tokenizer.json). " +
                "Install the model with 'Centurion models install sat-3l-sm'.");

        _sp = new SentencePieceTokenizer(spmPath);
        (_vocab, _sId, _eosId, _unkId) = ParseTokenizerJson(tokenizerPath);
    }

    /// <summary>
    /// Encodes a normalized text into model input ids plus per-token metadata.
    /// </summary>
    /// <param name="normalizedText">NFKC-normalized, whitespace-collapsed text (no leading/trailing spaces).</param>
    /// <returns>
    /// <c>Ids</c> includes the leading &lt;s&gt; and trailing &lt;/s&gt; special tokens; <c>Pieces</c> and
    /// <c>Offsets</c> cover only the content tokens, with <c>Offsets[i]</c> the start character index of
    /// piece <c>i</c> within <paramref name="normalizedText"/>.
    /// </returns>
    public (int[] Ids, string[] Pieces, int[] Offsets) Encode(string normalizedText)
    {
        var words = normalizedText.Split(' ').Where(w => w.Length > 0).ToList();
        var ids = new List<int> { _sId };
        var pieces = new List<string>();
        var offsets = new List<int>();

        var pos = 0;
        var first = true;
        foreach (var word in words)
        {
            var wordPieces = _sp.Tokenize(word).ToArray();
            // The first word starts at position 0; later words have one leading space in the text.
            var cursor = first ? pos : pos + 1;
            foreach (var p in wordPieces)
            {
                var charLen = p.Length > 1 && p[0] == '▁' ? p.Length - 1 : p.Length;
                offsets.Add(cursor);
                pieces.Add(p);
                ids.Add(_vocab.TryGetValue(p, out var id) ? id : _unkId);
                cursor += charLen;
            }
            pos += word.Length;
            if (!first) pos += 1;
            first = false;
        }

        ids.Add(_eosId);
        return (ids.ToArray(), pieces.ToArray(), offsets.ToArray());
    }

    /// <summary>Parses tokenizer.json: vocab array [piece, score] with id = array index, plus added tokens.</summary>
    private static (Dictionary<string, int> Vocab, int S, int Eos, int Unk) ParseTokenizerJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("model").GetProperty("vocab").EnumerateArray())
            vocab[entry[0].GetString()!] = vocab.Count;

        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in root.GetProperty("added_tokens").EnumerateArray())
            added[t.GetProperty("content").GetString()!] = t.GetProperty("id").GetInt32();

        int Get(string token, int fallback) => added.TryGetValue(token, out var id) ? id : fallback;
        return (vocab, Get("<s>", 0), Get("</s>", 2), Get("<unk>", 3));
    }
}
