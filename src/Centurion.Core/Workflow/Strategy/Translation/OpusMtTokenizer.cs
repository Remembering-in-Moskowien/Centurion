using System.Text.Json;
using SIL.Machine.Tokenization.SentencePiece;

namespace Centurion.Core.Workflow.Strategy.Translation;

/// <summary>
/// OPUS-MT tokenizer wrapper: SentencePiece (Unigram) subword tokenization plus the
/// piece &lt;-&gt; id mapping from the model's vocab.json. These OPUS-MT models share a single
/// SentencePiece model for both sides (separate_vocabs=false), so source.spm handles both
/// encoding and decoding. Behavior matches the HF MarianTokenizer: the source is encoded
/// without special tokens (the caller appends eos) and decoded piece-by-piece.
/// </summary>
internal sealed class OpusMtTokenizer
{
    private const int UnknownId = 1;
    private const string UnknownPiece = "<unk>";

    private readonly SentencePieceTokenizer _sp;
    private readonly SentencePieceDetokenizer _detokenizer = new();
    private readonly Dictionary<string, int> _pieceToId;
    private readonly Dictionary<int, string> _idToPiece;

    /// <summary>Creates a tokenizer from the model folder (requires source.spm and vocab.json).</summary>
    /// <param name="modelFolder">OPUS-MT model directory.</param>
    /// <exception cref="FileNotFoundException">When source.spm or vocab.json is missing.</exception>
    public OpusMtTokenizer(string modelFolder)
    {
        var spmPath = Path.Combine(modelFolder, "source.spm");
        if (!File.Exists(spmPath))
            throw new FileNotFoundException("OPUS-MT SentencePiece model not found (run 'Centurion models install <pair>' first).", spmPath);

        var vocabPath = Path.Combine(modelFolder, "vocab.json");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException("OPUS-MT vocab.json not found (run 'Centurion models install <pair>' first).", vocabPath);

        _sp = new SentencePieceTokenizer(spmPath);

        var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabPath))
            ?? throw new InvalidDataException($"OPUS-MT vocab file is empty or invalid: {vocabPath}");
        _pieceToId = new Dictionary<string, int>(vocab, StringComparer.Ordinal);
        _idToPiece = vocab
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.First().Key);
    }

    /// <summary>Encodes text into piece ids (no special tokens added).</summary>
    public IReadOnlyList<int> Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var ids = new List<int>();
        foreach (var piece in _sp.Tokenize(text))
        {
            ids.Add(_pieceToId.TryGetValue(piece, out var id) ? id : UnknownId);
        }
        return ids;
    }

    /// <summary>Decodes piece ids back into text.</summary>
    public string Decode(IReadOnlyList<int> ids)
    {
        var pieces = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            pieces.Add(_idToPiece.TryGetValue(id, out var piece) ? piece : UnknownPiece);
        }
        return _detokenizer.Detokenize(pieces);
    }
}
