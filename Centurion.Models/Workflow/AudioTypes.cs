namespace Centurion.Models.Workflow;

public enum AudioNoiseReductionBackend
{
    BuiltInFfmpeg,
    ExternalCli
}

public record AudioProbeInfo(int SampleRate, int Channels, string Codec, string Format);
