namespace Centurion.Core.Models;

/// <summary>
/// ASS字幕生成枚举标记
/// </summary>
[Flags]
public enum AssSubSpawnerOptions
{
    /// <summary>从SRT字幕文件生成</summary>
    Srt = 1 << 0,

    /// <summary>从音频文件语音识别生成</summary>
    Media = 1 << 1
}