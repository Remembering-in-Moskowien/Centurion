namespace Centurion.Core.Request;

/// <summary>
/// 媒体文件字幕生成算子请求载荷（合并原 Request 与 Options）
/// </summary>
public class MediaGenerationRequest
{
    // ---------- 输入 ----------
    public required string InputFilePath { get; init; }

    // ---------- Whisper 参数 ----------
    public string ModelName { get; init; } = "base";
    public string Language { get; init; } = "en";
    public string? InitialPrompt { get; init; }

    // ---------- Catalyst 分句参数 ----------
    public int MaxLength { get; init; } = 80;
    public int TargetLength { get; init; } = 50;
    public int SpreadRange { get; init; } = 10;

    // ---------- 说话人分割 ----------
    public int NumSpeakers { get; init; } = 0;   // 0 表示自动

    // ---------- 输出风格 ----------
    public bool Karaoke { get; init; } = false;

    // ---------- 强制对齐 ----------
    public bool Align { get; init; } = false;
    public string? AlignmentModelName { get; set; } = "wav2vec2-base-960h";
}