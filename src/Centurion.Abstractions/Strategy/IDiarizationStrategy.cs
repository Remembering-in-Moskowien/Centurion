using Centurion.Models.Workflow;
using Centurion.Abstractions;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Diarization result representing one speaker segment, measured in seconds.
/// </summary>
/// <param name="StartSeconds">Segment start time in seconds.</param>
/// <param name="EndSeconds">Segment end time in seconds.</param>
/// <param name="Speaker">Speaker label, such as "A", "B", or "SPEAKER_00".</param>
public sealed record SpeakerSegment(double StartSeconds, double EndSeconds, string Speaker);

/// <summary>
/// Diarization strategy that returns speaker segments for input audio.
/// The implementation selects the backend, such as polyvoice or WeSpeaker via sherpa-onnx.
/// </summary>
public interface IDiarizationStrategy
{
    /// <summary>
    /// Strategy name used to identify the active diarization backend in logs.
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Performs diarization on audio.
    /// </summary>
    /// <param name="audioPath">Path to preprocessed audio.</param>
    /// <param name="numSpeakers">Number of speakers; 0 enables automatic detection.</param>
    /// <param name="segmentModel">Backend-specific segmentation model; null uses the backend default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="device">Preferred inference device; defaults to automatic detection.</param>
    /// <returns>List of speaker segments.</returns>
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto);
}
