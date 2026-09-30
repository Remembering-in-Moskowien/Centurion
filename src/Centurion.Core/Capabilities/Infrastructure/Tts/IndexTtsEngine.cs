using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// 基于 IndexTTS-Rust（8b-is/IndexTTS-Rust，纯 Rust + ONNX Runtime）的本地 TTS 引擎。
/// 引擎二进制由用户放置于 tools/indextts/（与 VSF 相同捆绑策略，无自动下载）；
/// 模型经 <see cref="ModelManager"/> 下载自动可获部分（bigvgan + speaker_encoder ONNX 及外部权重），
/// gpt.onnx / s2mel.onnx / bpe.model 需由用户从原版 IndexTTS 检查点转换后放置于模型目录。
/// 合成命令：indextts clone -t &lt;text&gt; -v &lt;参考音频&gt; -o &lt;输出&gt; --model-dir &lt;目录&gt; --config &lt;config.yaml&gt;。
/// </summary>
/// <remarks>
/// 注意：上游 IndexTTS-Rust 当前 GPT 推理为占位实现（生成占位 mel），接入本引擎可跑通
/// 完整链路（模型管理/命令路由/输出文件），但合成音频为占位内容；待上游发布完整推理后
/// 替换引擎二进制即可获得真实合成。模型缺失时按项目惯例报错提示，不静默进入 demo 模式。
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

    /// <summary>模型注册表条目名（IndexTTS-Rust 2 代模型，bigvgan + speaker_encoder ONNX）。</summary>
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
    /// 确保模型就绪：自动下载 bigvgan/speaker_encoder ONNX（含外部权重），
    /// 检查用户提供的 gpt/s2mel/bpe 文件，并写入含绝对路径的 config.yaml。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模型目录（models/indextts/indextts2/）。</returns>
    /// <exception cref="TtsSynthesisException">模型不完整。</exception>
    private async Task<string> EnsureModelAsync(CancellationToken cancellationToken)
    {
        using var manager = new ModelManager(ModelKey, modelRegistry.IndexTtsModels, serviceProvider, "indextts");
        await manager.CheckHealthAsync(cancellationToken);
        var dir = manager.ModelFolder;

        // 用户提供/转换的部分（上游官方未发布 ONNX，需从原版 IndexTTS 检查点转换）
        var userRequired = new[] { "gpt.onnx", "s2mel.onnx", "bpe.model" };
        var missing = userRequired.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
        if (missing.Count > 0)
        {
            throw new TtsSynthesisException(
                $"IndexTTS models incomplete in {dir}: missing {string.Join(", ", missing)}. " +
                "Convert the original IndexTTS checkpoints to ONNX (python scripts/export_onnx.py --model gpt|s2mel) " +
                "and place gpt.onnx, s2mel.onnx and bpe.model there, then retry.");
        }

        // config.yaml：缺失时写入内置默认（路径全部绝对化，Rust 端按字符串直用）
        var configPath = Path.Combine(dir, "config.yaml");
        if (!File.Exists(configPath))
        {
            await File.WriteAllTextAsync(configPath, BuildConfig(dir), cancellationToken);
            logger.LogInformation("Wrote default IndexTTS config.yaml at {Config}", configPath);
        }

        return dir;
    }

    /// <summary>生成 IndexTTS-Rust config.yaml（字段与仓库默认一致，路径替换为绝对路径）。</summary>
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
