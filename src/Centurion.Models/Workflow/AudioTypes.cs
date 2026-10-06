namespace Centurion.Models.Workflow;

/// <summary>Selection of the noise reduction backend implementation.</summary>
public enum AudioNoiseReductionBackend
{
    /// <summary>Use the built-in FFmpeg filters for noise reduction.</summary>
    BuiltInFfmpeg,
    /// <summary>Invoke an external command-line tool for noise reduction.</summary>
    ExternalCli
}

/// <summary>Basic stream information obtained by audio probing.</summary>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Channels">Number of channels.</param>
/// <param name="Codec">Audio codec identifier.</param>
/// <param name="Format">Container/muxing format identifier.</param>
public record AudioProbeInfo(int SampleRate, int Channels, string Codec, string Format);
