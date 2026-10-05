using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// Local TTS engine based on IndexTTS-Rust (8b-is/IndexTTS-Rust, pure Rust + ONNX
/// Runtime). The engine binary is placed by the user under tools/indextts/ (the same
/// bundling policy as VSF; no auto-download). The model parts that are automatically
/// available are downloaded via <see cref="ModelManager"/> (bigvgan + speaker_encoder
/// ONNX and their external weights); gpt.onnx / s2mel.onnx / bpe.model must be converted
/// by the user from the original IndexTTS checkpoints and placed in the model directory.
/// Synthesis command: indextts clone -t &lt;text&gt; -v &lt;reference audio&gt; -o &lt;output&gt;
/// --model-dir &lt;dir&gt; --config &lt;config.yaml&gt;.
/// </summary>
/// <remarks>
/// Note: upstream IndexTTS-Rust's current GPT inference is a placeholder implementation
/// (it generates a placeholder mel). Wiring up this engine runs the full pipeline
/// end-to-end (model management / command routing / output file), but the synthesized
/// audio is placeholder content; once upstream ships full inference, replacing the engine
/// binary yields real synthesis. When the model is missing it reports an error per the
/// project's convention instead of silently falling into demo mode.
/// </remarks>
public sealed class IndexTtsEngine(
    IndexTtsManager indexTtsManager,
    ModelRegistry modelRegistry,
    IServiceProvider serviceProvider,
    ProcessManager processManager,
    ILogger<IndexTtsEngine> logger) : ITtsEngine
{
    /// <inheritdoc />
    public string EngineName => "indextts";

    /// <summary>Model registry entry name (IndexTTS-Rust 2nd-gen model, bigvgan + speaker_encoder ONNX).</summary>
    private const string ModelKey = "indextts2";

    /// <inheritdoc cref="ITtsEngine.SynthesizeAsync"/>
    public async Task<double> SynthesizeAsync(string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken)
    {
        var exe = await indexTtsManager.EnsureInstalledAsync(cancellationToken)
                  ?? throw new TtsSynthesisException("IndexTTS engine is not available: place indextts.exe under tools/indextts/ (build: cargo build --release).");
        if (string.IsNullOrWhiteSpace(referenceAudioPath))
            throw new TtsSynthesisException("IndexTTS is zero-shot voice cloning and requires a speaker reference audio; dub with --speaker-reference.");

        var modelDir = await EnsureModelAsync(cancellationToken);

        var args = new List<string>
        {
            "clone",
            "-t", text,
            "-v", referenceAudioPath,
            "-o", outputWavPath,
            "--model-dir", modelDir,
            "--config", Path.Combine(modelDir, "config.yaml")
        };

        var output = await processManager.ExecuteAsync(exe, args, cancellationToken);
        if (!File.Exists(outputWavPath))
            throw new TtsSynthesisException($"IndexTTS produced no output for: {Truncate(text, 60)}");
        logger.LogDebug("indextts synthesized {Output} ({Text})", outputWavPath, Truncate(text, 60));
        _ = output;
        return 0;
    }

    /// <summary>
    /// Ensures the model is ready: auto-downloads the bigvgan/speaker_encoder ONNX (with
    /// their external weights), checks the user-supplied gpt/s2mel/bpe files, and writes
    /// a config.yaml containing absolute paths.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model directory (models/indextts/indextts2/).</returns>
    /// <exception cref="TtsSynthesisException">The model is incomplete.</exception>
    private async Task<string> EnsureModelAsync(CancellationToken cancellationToken)
    {
        using var manager = new ModelManager(ModelKey, modelRegistry.IndexTtsModels, serviceProvider, "indextts");
        await manager.CheckHealthAsync(cancellationToken);
        var dir = manager.ModelFolder;

        // User-supplied/converted parts (upstream has not published ONNX; must be converted from the original IndexTTS checkpoints)
        var userRequired = new[] { "gpt.onnx", "s2mel.onnx", "bpe.model" };
        var missing = userRequired.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
        if (missing.Count > 0)
        {
            throw new TtsSynthesisException(
                $"IndexTTS models incomplete in {dir}: missing {string.Join(", ", missing)}. " +
                "Convert the original IndexTTS checkpoints to ONNX (python scripts/export_onnx.py --model gpt|s2mel) " +
                "and place gpt.onnx, s2mel.onnx and bpe.model there, then retry.");
        }

        // config.yaml: when missing, write the built-in default (all paths made absolute, since the Rust side uses them verbatim)
        var configPath = Path.Combine(dir, "config.yaml");
        if (!File.Exists(configPath))
        {
            await File.WriteAllTextAsync(configPath, BuildConfig(dir), cancellationToken);
            logger.LogInformation("Wrote default IndexTTS config.yaml at {Config}", configPath);
        }

        return dir;
    }

    /// <summary>Generates the IndexTTS-Rust config.yaml (fields match the repo defaults, with paths replaced by absolute paths).</summary>
    private static string BuildConfig(string dir) => $$"""
        gpt:
          layers: 8
          model_dim: 512
          heads: 8
          max_text_tokens: 120
          max_mel_tokens: 250
          stop_mel_token: 8193
          start_text_token: 8192
          start_mel_token: 8192
          num_mel_codes: 8194
          num_text_tokens: 6681
        vocoder:
          name: bigvgan_v2_22khz_80band_256x
          use_fp16: true
          use_deepspeed: false
        s2mel:
          checkpoint: {{dir}}/s2mel.onnx
          preprocess:
            sr: 22050
            n_fft: 1024
            hop_length: 256
            win_length: 1024
            n_mels: 80
            fmin: 0.0
            fmax: 8000.0
        dataset:
          bpe_model: {{dir}}/bpe.model
          vocab_size: 6681
        emotions:
          num_dims: 8
          num: [5, 6, 8, 6, 5, 4, 7, 6]
          matrix_path: {{dir}}/emotion_matrix.safetensors
        inference:
          device: cpu
          use_fp16: false
          batch_size: 1
          top_k: 50
          top_p: 0.95
          temperature: 1.0
          repetition_penalty: 1.0
          length_penalty: 1.0
        model_dir: {{dir}}
        """;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
