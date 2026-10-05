using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// Local TTS engine based on llama.cpp <c>llama-tts</c> (Qwen3-TTS 1.7B Base GGUF).
/// Both the tool and the model are auto-downloaded on demand: when llama-tts is missing,
/// <see cref="LlamaTtsManager"/> pulls the official zip; the model is downloaded via
/// <see cref="ModelManager"/> (models/tts/ directory) as two GGUF files, talker +
/// tokenizer. On synthesis failure (non-zero exit / missing model) it throws
/// <see cref="TtsSynthesisException"/>, which the caller logs as a Warning and uses to
/// skip that sentence.
/// </summary>
public sealed class LlamaTtsEngine(
    LlamaTtsManager llamaTtsManager,
    ModelRegistry modelRegistry,
    IServiceProvider serviceProvider,
    ProcessManager processManager,
    ILogger<LlamaTtsEngine> logger) : ITtsEngine
{
    /// <inheritdoc />
    public string EngineName => "llama";

    /// <summary>
    /// Synthesizes a single sentence of speech: llama-tts -m backbone.gguf -mm mmproj.gguf
    /// --tts-lang &lt;language&gt; -p text (optionally with a --tts-speaker-file reference audio).
    /// </summary>
    /// <remarks>
    /// llama-tts arguments follow the official Qwen3-TTS/llama.cpp integration docs; if the
    /// argument names in your target build have changed, run it once and calibrate the
    /// constants <see cref="ArgModel"/>, <see cref="ArgMmproj"/>, etc. against
    /// <c>llama-tts --help</c>.
    /// </remarks>
    /// <inheritdoc cref="ITtsEngine.SynthesizeAsync"/>
    public async Task<double> SynthesizeAsync(string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken)
    {
        var exe = await llamaTtsManager.EnsureInstalledAsync(cancellationToken)
                  ?? throw new TtsSynthesisException("llama-tts is not available (auto-download failed).");

        var modelDir = await EnsureModelAsync(cancellationToken);
        var backbone = Path.Combine(modelDir, "Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf");
        var mmproj = Path.Combine(modelDir, "mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf");

        var args = new List<string>
        {
            ArgModel, backbone,
            ArgMmproj, mmproj,
            ArgLanguage, language,
            ArgOutput, outputWavPath,
            ArgPrompt, text
        };
        if (!string.IsNullOrWhiteSpace(referenceAudioPath))
        {
            args.Add(ArgReference);
            args.Add(referenceAudioPath);
        }

        var output = await processManager.ExecuteAsync(exe, args, cancellationToken);
        if (!File.Exists(outputWavPath))
            throw new TtsSynthesisException($"llama-tts produced no output for: {Truncate(text, 60)}");
        logger.LogDebug("llama-tts synthesized {Output} ({Text})", outputWavPath, Truncate(text, 60));
        _ = output;
        return 0;
    }

    private async Task<string> EnsureModelAsync(CancellationToken cancellationToken)
    {
        using var manager = new ModelManager("1.7b-base-q4", modelRegistry.Qwen3TtsModels, serviceProvider, "tts");
        await manager.CheckHealthAsync(cancellationToken);
        return manager.ModelFolder;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    // llama-tts argument names (Qwen3-TTS / llama.cpp integration, verified on b11118). Recalibrate against --help if the build version changes.
    private const string ArgModel = "-m";
    private const string ArgMmproj = "-mm";
    private const string ArgLanguage = "--tts-lang";
    private const string ArgReference = "--tts-speaker-file";
    private const string ArgOutput = "-o";
    private const string ArgPrompt = "-p";
}
