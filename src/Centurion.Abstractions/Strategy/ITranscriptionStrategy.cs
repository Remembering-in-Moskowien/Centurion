using Centurion.Models.Workflow;
using Centurion.Abstractions;
using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Transcription strategy interface supporting local, API-based, CLI, and other engines.
/// </summary>
public interface ITranscriptionStrategy
{
    /// <summary>
    /// Transcribes audio and returns word-level timestamps.
    /// </summary>
    /// <param name="audioPath">Input audio path, converted to 16 kHz mono WAV.</param>
    /// <param name="language">Language code, such as "en" or "zh".</param>
    /// <param name="modelName">Model name, such as tiny, base, or small.</param>
    /// <param name="initialPrompt">Optional initial prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="device">Preferred inference device; defaults to automatic detection and selects a matching GPU tool variant when available.</param>
    /// <returns>Words with text and start/end times in milliseconds.</returns>
    Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto);

    /// <summary>
    /// Strategy name used in logs.
    /// </summary>
    string StrategyName { get; }
}