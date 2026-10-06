using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Scheme one: speaker diarization built into CrispASR (an existing tool).
/// Supports energy / xcorr / vad-turns / foxnose (default foxnose: most accurate, no stereo needed, auto-estimates speaker count).
/// </summary>
public sealed class CrispAsrDiarizationStrategy(IServiceProvider serviceProvider)
    : CrispAsrDiarizationBase(serviceProvider)
{
    /// <summary>Display name of the strategy.</summary>
    public override string StrategyName => "CrispASR Diarization";

    /// <summary>Uses foxnose by default (WeSpeaker embeddings + spectral clustering, no external dependencies).</summary>
    public string Method { get; set; } = "foxnose";

    /// <summary>Method name passed to --diarize-method, taken from the configurable <see cref="Method"/>.</summary>
    protected override string DiarizeMethod => Method;
}
