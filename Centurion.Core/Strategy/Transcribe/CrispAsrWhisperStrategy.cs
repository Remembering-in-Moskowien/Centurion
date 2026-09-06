// Centurion.Core/Strategies/Transcription/CrispAsrWhisperStrategy.cs

namespace Centurion.Core.Strategy.Transcribe;

public class CrispAsrWhisperStrategy : CrispAsrBaseStrategy
{
    public override string StrategyName => "CrispASR (Whisper)";

    public CrispAsrWhisperStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    protected override string GetBackendName() => "whisper";

    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "base";
        return await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
    }

    // No aligner for Whisper (override to return null)
    protected override Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}