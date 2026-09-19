namespace Centurion.Models.Workflow;

/// <summary>音频预处理流水线的各项开关与阈值配置。</summary>
public record AudioPreprocessConfig
{
    /// <summary>是否启用重采样到模型要求的采样率，默认开启。</summary>
    public bool EnableResampling { get; init; } = true;
    /// <summary>是否启用多声道混音为单声道，默认开启。</summary>
    public bool EnableDownmixing { get; init; } = true;
    /// <summary>是否启用高通滤波以去除低频噪声，默认开启。</summary>
    public bool EnableHighPass { get; init; } = true;
    /// <summary>是否启用响度归一化，默认开启。</summary>
    public bool EnableLoudnessNormalization { get; init; } = true;
    /// <summary>是否启用噪声抑制，默认关闭。</summary>
    public bool EnableNoiseReduction { get; init; } = false;
    /// <summary>判定为低信噪比而触发噪声处理的阈值（分贝），默认 15。</summary>
    public double SnrThresholdDb { get; init; } = 15.0;
    /// <summary>噪声抑制所使用的后端实现。</summary>
    public AudioNoiseReductionBackend NoiseReductionBackend { get; init; } = AudioNoiseReductionBackend.BuiltInFfmpeg;
}
