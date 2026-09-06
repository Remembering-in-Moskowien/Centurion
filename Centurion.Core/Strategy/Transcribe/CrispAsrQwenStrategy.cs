// Centurion.Core/Strategies/Transcription/CrispAsrQwenStrategy.cs

using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Transcribe;

public class CrispAsrQwenStrategy : CrispAsrBaseStrategy
{
    public override string StrategyName => "CrispASR (Qwen3)";

    private const string DefaultAlignerModel = "qwen3-forced-aligner-0.6b";

    public CrispAsrQwenStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    protected override string GetBackendName() => "qwen3";

    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "qwen3-asr-0.6b";
        return await _modelResolver.GetQwen3AsrModelPathAsync(modelName, cancellationToken);
    }

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