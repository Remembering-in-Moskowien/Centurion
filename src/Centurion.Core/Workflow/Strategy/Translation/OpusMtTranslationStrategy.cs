using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Strategy.Translation;

/// <summary>
/// Local machine-translation strategy backed by OPUS-MT (Helsinki-NLP) ONNX models.
/// Runs fully offline after the first-use model download: SentencePiece tokenization,
/// encoder-decoder ONNX inference with beam search, and detokenization. The model is
/// downloaded automatically on first use (via the model registry / <c>models install</c>);
/// the timeline and word-level details of each sentence are never touched.
/// Falls back to 1:1 target-script alignment when a matching script is supplied.
/// </summary>
public sealed class OpusMtTranslationStrategy : ITranslationStrategy
{
    private readonly ModelManager _modelManager;
    private readonly int _beamSize;
    private readonly int _maxLength;
    private readonly ILogger<OpusMtTranslationStrategy>? _logger;
    private readonly Lazy<OpusMtEngine> _engine;

    /// <summary>Creates an OPUS-MT translation strategy.</summary>
    /// <param name="modelManager">Model manager for the OPUS-MT pair (downloads the model on first use).</param>
    /// <param name="beamSize">Beam size for decoding; 1 = greedy.</param>
    /// <param name="maxLength">Maximum decoded token count per line.</param>
    /// <param name="logger">Optional logger; when null, nothing is logged.</param>
    public OpusMtTranslationStrategy(
        ModelManager modelManager,
        int beamSize,
        int maxLength,
        ILogger<OpusMtTranslationStrategy>? logger = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _beamSize = Math.Max(1, beamSize);
        _maxLength = Math.Max(1, maxLength);
        _logger = logger;
        _engine = new Lazy<OpusMtEngine>(() => new OpusMtEngine(modelManager.ModelFolder));
    }

    /// <summary>Display name of the strategy.</summary>
    public string StrategyName => "OPUS-MT";

    /// <summary>
    /// Translates the sentences: first tries 1:1 target-script alignment, otherwise runs the
    /// local OPUS-MT model line by line (parallel up to <see cref="TranslationOptions.MaxConcurrency"/>).
    /// </summary>
    /// <param name="sentences">The sentences to translate; translations are filled in place and timings are unchanged.</param>
    /// <param name="options">Translation options: target language, glossary, target-language script, concurrency.</param>
    /// <param name="cancellationToken">Token used to cancel the translation process.</param>
    /// <returns>The sentence list after translation.</returns>
    public async Task<List<Sentence>> TranslateAsync(
        List<Sentence> sentences,
        TranslationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (sentences.Count == 0)
            return sentences;

        if (LLMTranslationStrategy.AlignToScript(sentences, options.TargetScriptLines))
        {
            _logger?.LogInformation("Target script matches {Count} sentences; using 1:1 script alignment.", sentences.Count);
            return sentences;
        }

        // Auto-download the model on first use; then lazily create the inference engine.
        await _modelManager.EnsureInstalledAsync(cancellationToken);
        var engine = _engine.Value;

        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        await Task.WhenAll(sentences.Select(async sentence =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var text = sentence.Text;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var translation = await Task.Run(() => engine.Translate(text, _beamSize, _maxLength), cancellationToken);
                if (!string.IsNullOrWhiteSpace(translation))
                    sentence.TranslatedText = translation;
                else
                    _logger?.LogWarning("Keeping original text for untranslatable sentence: {Text}", text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "OPUS-MT translation failed for sentence: {Text}", sentence.Text);
            }
            finally
            {
                gate.Release();
            }
        }));

        return sentences;
    }
}
