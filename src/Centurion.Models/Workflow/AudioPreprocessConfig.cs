namespace Centurion.Models.Workflow;

/// <summary>Configuration of toggles and thresholds for the audio preprocessing pipeline.</summary>
public record AudioPreprocessConfig
{
    /// <summary>Whether to enable resampling to the sample rate required by the model; enabled by default.</summary>
    public bool EnableResampling { get; init; } = true;
    /// <summary>Whether to enable downmixing multiple channels to mono; enabled by default.</summary>
    public bool EnableDownmixing { get; init; } = true;
    /// <summary>Whether to enable a high-pass filter to remove low-frequency noise; enabled by default.</summary>
    public bool EnableHighPass { get; init; } = true;
    /// <summary>Whether to enable loudness normalization; enabled by default.</summary>
    public bool EnableLoudnessNormalization { get; init; } = true;
    /// <summary>Whether to enable noise reduction; disabled by default.</summary>
    public bool EnableNoiseReduction { get; init; } = false;
    /// <summary>Threshold in decibels below which low SNR triggers noise processing; defaults to 15.</summary>
    public double SnrThresholdDb { get; init; } = 15.0;
    /// <summary>The backend implementation used for noise reduction.</summary>
    public AudioNoiseReductionBackend NoiseReductionBackend { get; init; } = AudioNoiseReductionBackend.BuiltInFfmpeg;
}
