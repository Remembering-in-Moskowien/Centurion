using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// 基于 llama.cpp <c>llama-tts</c>（Qwen3-TTS 1.7B Base GGUF）的本地 TTS 引擎。
/// 工具与模型均按需自动下载：llama-tts 缺失时经 <see cref="LlamaTtsManager"/> 拉取官方 zip，
/// 模型经 <see cref="ModelManager"/>（models/tts/ 目录）下载 talker + tokenizer 两个 GGUF。
/// 合成失败（非零退出/缺模型）抛 <see cref="TtsSynthesisException"/>，由上层记录 Warning 后跳过该句。
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
    /// 合成单句语音：llama-tts -m backbone.gguf -mm mmproj.gguf --tts-lang &lt;语言&gt; -p 文本（可带 --tts-speaker-file 参考音频）。
    /// </summary>
    /// <remarks>
    /// llama-tts 参数以官方 Qwen3-TTS/llama.cpp 集成文档为准；若目标构建的参数名有变动，
    /// 运行一次后按 <c>llama-tts --help</c> 校准 <see cref="ArgModel"/>、<see cref="ArgMmproj"/> 等常量即可。
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

    // llama-tts 参数名（Qwen3-TTS / llama.cpp 集成，b11118 实测）。如构建版本变化，按 --help 校准即可。
    private const string ArgModel = "-m";
    private const string ArgMmproj = "-mm";
    private const string ArgLanguage = "--tts-lang";
    private const string ArgReference = "--tts-speaker-file";
    private const string ArgOutput = "-o";
    private const string ArgPrompt = "-p";
}
