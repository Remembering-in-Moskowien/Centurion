using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Tts;

/// <summary>
/// 基于 QORA-TTS（incordlabs/QORA-TTS-12Hz-1.7B，纯 Rust 的 Qwen3-TTS 推理引擎）的本地 TTS 引擎。
/// 工具与模型自包含于 tools/qora-tts/（首次使用自动下载，含 1.56GB Q4 权重）。
/// 合成命令：qora-tts.exe --ref-audio &lt;参考wav&gt; --text &lt;文本&gt; --language &lt;语言&gt; --output &lt;输出.wav&gt;。
/// 支持 10 语言与 3-10 秒参考音频音色克隆；无参考音频时按 dub 惯例报错提示 --speaker-reference。
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

    /// <summary>把 ISO 639-1 语言码映射为 QORA-TTS 的语言名（未知回退 english）。</summary>
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
