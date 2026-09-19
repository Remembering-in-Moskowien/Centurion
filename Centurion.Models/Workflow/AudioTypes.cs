namespace Centurion.Models.Workflow;

/// <summary>噪声抑制后端实现的选择。</summary>
public enum AudioNoiseReductionBackend
{
    /// <summary>使用 FFmpeg 内置滤镜进行降噪。</summary>
    BuiltInFfmpeg,
    /// <summary>调用外部命令行工具进行降噪。</summary>
    ExternalCli
}

/// <summary>音频探测（probe）得到的基础流信息。</summary>
/// <param name="SampleRate">采样率（Hz）。</param>
/// <param name="Channels">声道数。</param>
/// <param name="Codec">音频编码格式标识。</param>
/// <param name="Format">容器/封装格式标识。</param>
public record AudioProbeInfo(int SampleRate, int Channels, string Codec, string Format);
