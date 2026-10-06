using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Workflow.Strategy.VocalSeparation;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Vocal separation operator (native htdemucs ONNX Runtime inference, optional enhancement).
/// Separates the vocals track from the audio and writes it to State.VocalsPath, for preferential
/// consumption by transcription and speaker diarization. Runs only when WorkflowConfig.VocalSeparation
/// is enabled; a failure is non-fatal: it only logs a warning and falls back to the original audio.
/// <para>
/// Model acquisition: the MIT-licensed StemSplitio htdemucs ONNX export (single-file, 4 stems) is
/// downloaded automatically through the HF mirror chain into models/htdemucs on first use. No python,
/// no external separation CLI.
/// </para>
/// </summary>
public sealed class VocalSeparationOperator(
    HtDemucsOnnxVocalSeparator separator,
    ILogger<VocalSeparationOperator> logger) : PipelineOperatorBase<VocalSeparationOperator>(logger)
{
    private readonly HtDemucsOnnxVocalSeparator _separator = separator ?? throw new ArgumentNullException(nameof(separator));

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Vocal Separation";

    /// <summary>
    /// Runs vocal separation: when the switch is on and no existing artifact is present, uses native
    /// htdemucs ONNX inference to separate the vocals track from the input audio, and writes the result
    /// into the <see cref="SubtitleWorkflowContext"/> state; a separation failure is non-fatal, only
    /// logging a warning and falling back to the original audio.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing configuration, state, and the input audio path.</param>
    /// <param name="cancellationToken">Token used to cancel the vocal separation process.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. Switch (off by default: no value for pure speech material and time-consuming)
        if (!config.VocalSeparation)
        {
            LogInfo("Vocal separation disabled (VocalSeparation = false).");
            return;
        }

        // 2. Checkpoint: skip when already done and the artifact exists
        if (context.State.IsVocalsSeparated
            && !string.IsNullOrEmpty(context.State.VocalsPath)
            && File.Exists(context.State.VocalsPath))
        {
            LogInfo("Vocal separation already exists, skipping.");
            return;
        }

        // 3. Input audio (prefer the preprocessed audio, fall back to the converted audio)
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for vocal separation: {inputPath}");
            return;
        }

        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping vocal separation.");
            return;
        }

        try
        {
            OnProgress(10, "Separating vocals with htdemucs (ONNX Runtime)...");
            var finalPath = Path.Combine(tempDir, $"vocals_{Guid.NewGuid():N}.wav");
            await _separator.SeparateVocalsAsync(
                inputPath, finalPath, config.VocalSeparationModel, config.Device, cancellationToken);

            context.State.VocalsPath = finalPath;
            context.State.IsVocalsSeparated = true;
            OnProgress(100, "Vocal separation completed.");
            LogInfo($"Vocals saved to: {finalPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: a separation failure does not interrupt subtitle generation
            LogWarning($"Vocal separation failed; continuing with original audio. {ex.Message}");
        }
    }
}
