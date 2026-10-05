using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// Local TTS engine based on QORA-TTS (incordlabs/QORA-TTS-12Hz-1.7B, a pure-Rust
/// Qwen3-TTS inference engine). The tool and model are self-contained under
/// tools/qora-tts/ (auto-downloaded on first use, including the 1.56GB Q4 weights).
/// Synthesis command: qora-tts.exe --ref-audio &lt;reference wav&gt; --text &lt;text&gt; --language
/// &lt;language&gt; --output &lt;output.wav&gt;. Supports 10 languages and voice cloning from a
/// 3–10 second reference audio; with no reference audio it reports an error per the dub
/// convention, pointing to --speaker-reference.
/// </summary>
public sealed class QoraTtsEngine(
    QoraTtsManager qoraTtsManager,
    ProcessManager processManager,
    ILogger<QoraTtsEngine> logger) : ITtsEngine
{
    /// <inheritdoc />
    public string EngineName => "qora";

    /// <inheritdoc cref="ITtsEngine.SynthesizeAsync"/>
    public async Task<double> SynthesizeAsync(string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken)
    {
        var exe = await qoraTtsManager.EnsureInstalledAsync(cancellationToken)
                  ?? throw new TtsSynthesisException("QORA-TTS engine is not available: download failed or tools/qora-tts missing.");
        if (string.IsNullOrWhiteSpace(referenceAudioPath))
            throw new TtsSynthesisException("QORA-TTS (voice cloning) requires a speaker reference audio; dub with --speaker-reference.");

        var args = new List<string>
        {
            "--ref-audio", referenceAudioPath,
            "--text", text,
            "--language", MapLanguage(language),
            "--output", outputWavPath
        };

        var output = await processManager.ExecuteAsync(exe, args, cancellationToken);
        if (!File.Exists(outputWavPath))
            throw new TtsSynthesisException($"QORA-TTS produced no output for: {Truncate(text, 60)}");
        logger.LogDebug("qora-tts synthesized {Output} ({Text})", outputWavPath, Truncate(text, 60));
        _ = output;
        return 0;
    }

    /// <summary>Maps an ISO 639-1 language code to a QORA-TTS language name (falls back to english when unknown).</summary>
    private static string MapLanguage(string language) => (language ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" or "cn" or "chinese" => "chinese",
        "en" or "english" => "english",
        "de" or "german" => "german",
        "it" or "italian" => "italian",
        "pt" or "portuguese" => "portuguese",
        "es" or "spanish" => "spanish",
        "ja" or "japanese" => "japanese",
        "ko" or "korean" => "korean",
        "fr" or "french" => "french",
        "ru" or "russian" => "russian",
        _ => "english"
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
