using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Strategy.Transcribe;

/// <summary>
/// Transcription strategy based on the CrispASR Qwen3 backend: transcribes with the qwen3-asr model, with the Qwen3 forced aligner attached.
/// </summary>
public class CrispAsrQwenStrategy : CrispAsrBaseStrategy
{
    /// <summary>Display name of the strategy.</summary>
    public override string StrategyName => "CrispASR (Qwen3)";

    private const string DefaultAlignerModel = "qwen3-forced-aligner-0.6b";

    /// <summary>Creates a Qwen3 transcription strategy instance.</summary>
    /// <param name="serviceProvider">The container used to resolve dependency services.</param>
    public CrispAsrQwenStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    /// <summary>The backend name passed to CrispASR, fixed to qwen3.</summary>
    protected override string GetBackendName() => "qwen3";

    /// <summary>Resolves the local path of the Qwen3 ASR model; falls back to the default model when the name is empty.</summary>
    /// <param name="modelName">The requested model name.</param>
    /// <param name="cancellationToken">Token used to cancel the path resolution.</param>
    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "qwen3-asr-0.6b";
        return await _modelResolver.GetQwen3AsrModelPathAsync(modelName, cancellationToken);
    }

    /// <summary>Resolves the local path of the Qwen3 forced aligner; returns null on failure to skip alignment.</summary>
    /// <param name="cancellationToken">Token used to cancel the path resolution.</param>
    protected override async Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            var alignerPath = await _modelResolver.GetQwen3ForcedAlignerPathAsync(DefaultAlignerModel, cancellationToken);
            return alignerPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Qwen3 aligner model. Alignment disabled.");
            return null;
        }
    }
}