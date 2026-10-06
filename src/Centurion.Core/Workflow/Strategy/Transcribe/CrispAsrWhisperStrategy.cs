namespace Centurion.Core.Workflow.Strategy.Transcribe;

/// <summary>
/// Transcription strategy based on the CrispASR Whisper backend: transcribes with a Whisper model, with no forced aligner attached.
/// </summary>
public class CrispAsrWhisperStrategy : CrispAsrBaseStrategy
{
    /// <summary>Display name of the strategy.</summary>
    public override string StrategyName => "CrispASR (Whisper)";

    /// <summary>Creates a Whisper transcription strategy instance.</summary>
    /// <param name="serviceProvider">The container used to resolve dependency services.</param>
    public CrispAsrWhisperStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    /// <summary>The backend name passed to CrispASR, fixed to whisper.</summary>
    protected override string GetBackendName() => "whisper";

    /// <summary>Resolves the local path of the Whisper model; falls back to the default model when the name is empty.</summary>
    /// <param name="modelName">The requested model name.</param>
    /// <param name="cancellationToken">Token used to cancel the path resolution.</param>
    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "base";
        return await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
    }

    /// <summary>The Whisper backend uses no aligner, so this always returns null.</summary>
    /// <param name="cancellationToken">Cancellation token (unused in this implementation).</param>
    protected override Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}