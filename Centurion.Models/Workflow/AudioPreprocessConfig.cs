namespace Centurion.Models.Workflow;

public record AudioPreprocessConfig
{
    public bool EnableResampling { get; init; } = true;
    public bool EnableDownmixing { get; init; } = true;
    public bool EnableHighPass { get; init; } = true;
    public bool EnableLoudnessNormalization { get; init; } = true;
    public bool EnableNoiseReduction { get; init; } = false;
    public double SnrThresholdDb { get; init; } = 15.0;
    public AudioNoiseReductionBackend NoiseReductionBackend { get; init; } = AudioNoiseReductionBackend.BuiltInFfmpeg;
}
