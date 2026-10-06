using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Scheme two: Pyannote segmentation + TitaNet embeddings (both native to the CrispASR CLI, no Python needed).
/// --diarize-method pyannote (native GGUF segmentation) + --diarize-embedder auto (TitaNet speaker embeddings,
/// auto-downloaded by CrispASR), providing globally stable speaker IDs for long audio.
/// </summary>
public sealed class PyannoteTitaNetDiarizationStrategy(IServiceProvider serviceProvider)
    : CrispAsrDiarizationBase(serviceProvider)
{
    /// <summary>Display name of the strategy.</summary>
    public override string StrategyName => "Pyannote + TitaNet Diarization";

    /// <summary>Method name passed to --diarize-method, fixed to pyannote.</summary>
    protected override string DiarizeMethod => "pyannote";

    /// <summary>Embedder passed to --diarize-embedder, fixed to auto (auto-downloads the TitaNet GGUF).</summary>
    protected override string? DiarizeEmbedder => "auto";

    /// <summary>Default pyannote segmentation model name (overridable via the DiarizationModel configuration).</summary>
    protected override string? DefaultSegmentModel => "pyannote-seg-3.0";
}
