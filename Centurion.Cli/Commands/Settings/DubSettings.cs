using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>dub</c> 命令设置：媒体译制（Centurion 中间文件 → 译制 wav + 中间文件）。
/// Phase 2/3 扩展：伴奏混音（ducking）、响度归一化、TTS 并行、长句分块、重叠降级。
/// </summary>
public sealed class DubSettings : CommandSettings
{
    /// <summary>输入的 Centurion 中间文件（含句子、翻译与说话人信息）。</summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    public required FileInfo CenturionFile { get; set; }

    /// <summary>可选：原媒体文件（用于说话人参考音频提取；也作为输入时长基准）。</summary>
    [CommandOption("--media <FILE>")]
    public FileInfo? MediaFile { get; set; }

    /// <summary>可选：手动指定说话人参考音频目录（每说话人一个 wav，文件名 SPEAKER_xx.wav）。</summary>
    [CommandOption("--speaker-reference <DIR>")]
    public DirectoryInfo? SpeakerReference { get; set; }

    /// <summary>配音目标语言（ISO 639-1，如 zh/en/ja/de）。</summary>
    [CommandOption("--target-language <LANG>")]
    public string TargetLanguage { get; set; } = "zh";

    /// <summary>TTS 引擎（当前仅 "llama"）。</summary>
    [CommandOption("--tts-engine <ENGINE>")]
    public string TtsEngine { get; set; } = "llama";

    /// <summary>TTS 模型名（metadata.json Models 中注册的键，如 1.7b-base-q4）。</summary>
    [CommandOption("--tts-model <MODEL>")]
    public string TtsModel { get; set; } = "1.7b-base-q4";

    /// <summary>输出中间文件路径；缺省为输入名 .dub.centurion.json（wav 另以 .dub.wav 输出）。</summary>
    [CommandOption("-o|--output <FILE>")]
    public FileInfo? OutputFile { get; set; }

    /// <summary>严格时间对齐：超出 0.5x-2.0x 可调范围时钳制到边界（默认开；关闭则保留原合成时长）。</summary>
    [CommandOption("--strict-timing")]
    public bool StrictTiming { get; set; } = true;

    /// <summary>可选：伴奏/背景音频（原声带或配乐）；提供后启用 sidechain ducking 混音。</summary>
    [CommandOption("--background <FILE>")]
    public FileInfo? BackgroundFile { get; set; }

    /// <summary>禁用 ducking（有 --background 时默认开启）。</summary>
    [CommandOption("--no-ducking")]
    public bool NoDucking { get; set; }

    /// <summary>输出响度目标（LUFS，默认 -16；配合 loudnorm 归一化）。</summary>
    [CommandOption("--loudness-target <LUFS>")]
    public double LoudnessTarget { get; set; } = -16;

    /// <summary>TTS 合成并行度（默认 2；CPU 内存吃紧时调 1）。</summary>
    [CommandOption("--tts-parallelism <N>")]
    public int TtsParallelism { get; set; } = 2;

    /// <summary>长句分块阈值（秒，默认 15；目标时长超过时按比例拆分子段合成再拼接）。</summary>
    [CommandOption("--max-chunk-seconds <S>")]
    public double MaxChunkSeconds { get; set; } = 15;
}
