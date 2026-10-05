using Centurion.Models.Providers;
namespace Centurion.Abstractions.Providers;

/// <summary>
/// Shared contract for all providers: name, capability declaration, and availability checks.
/// Providers for ASR, OCR, LLM, TTS, diarization, and vocal separation
/// extend this interface with domain-specific execution methods.
/// </summary>
public interface IProvider
{
    /// <summary>Stable identifier, such as "whispercpp", "openai", or "zhipu", used by configuration and commands.</summary>
    string Name { get; }

    /// <summary>Human-readable display name, used by commands such as providers list.</summary>
    string DisplayName { get; }

    /// <summary>Capabilities, including local/cloud execution, languages, GPU, cost, latency, and quality.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Checks availability: cloud providers validate the API key and endpoint, while local providers check required tools and models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when available, or false when unavailable; does not throw.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Speech recognition (ASR) provider that returns word-level timestamps for audio.
/// </summary>
public interface IAsrProvider : IProvider
{
    /// <summary>
    /// Transcribes audio.
    /// </summary>
    /// <param name="audioPath">Input audio file path (16 kHz mono WAV).</param>
    /// <param name="language">Language code, such as en or zh.</param>
    /// <param name="model">Model name; null uses the provider default.</param>
    /// <param name="initialPrompt">Optional initial prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Word-level results and usage statistics.</returns>
    Task<ProviderResult<IReadOnlyList<Centurion.Models.Word>>> TranscribeAsync(
        string audioPath,
        string language,
        string? model,
        string? initialPrompt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optical character recognition (OCR) provider that extracts subtitle text from images.
/// </summary>
public interface IOcrProvider : IProvider
{
    /// <summary>
    /// Runs OCR on a single image.
    /// </summary>
    /// <param name="imagePath">Image file path.</param>
    /// <param name="model">Model name; null uses the provider default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted subtitle text; placeholder text may be returned when no subtitles are found.</returns>
    Task<ProviderResult<string>> OcrImageAsync(
        string imagePath,
        string? model,
        CancellationToken cancellationToken);
}

/// <summary>LLM conversation message.</summary>
/// <param name="Role">Message role: system, user, or assistant.</param>
/// <param name="Content">Message content.</param>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Large language model (LLM) provider with a unified chat/completions abstraction.
/// </summary>
public interface ILlmProvider : IProvider
{
    /// <summary>
    /// Sends a conversation and returns the assistant response.
    /// </summary>
    /// <param name="messages">Conversation messages.</param>
    /// <param name="model">Model name; null uses the provider default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response text and usage statistics, including tokens and cost.</returns>
    Task<ProviderResult<string>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model,
        CancellationToken cancellationToken);
}

/// <summary>
/// Text-to-speech (TTS) provider that synthesizes a single utterance.
/// </summary>
public interface ITtsProvider : IProvider
{
    /// <summary>
    /// Synthesizes one utterance.
    /// </summary>
    /// <param name="text">Text in the target language.</param>
    /// <param name="referenceAudioPath">Speaker reference audio path; null uses the default voice.</param>
    /// <param name="language">Target language (ISO 639-1).</param>
    /// <param name="outputWavPath">Output WAV path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Synthesized audio duration in seconds and usage statistics.</returns>
    Task<ProviderResult<double>> SynthesizeAsync(
        string text,
        string? referenceAudioPath,
        string language,
        string outputWavPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Diarization provider that returns speaker segments for audio.
/// </summary>
public interface IDiarizationProvider : IProvider
{
    /// <summary>
    /// Performs diarization on audio.
    /// </summary>
    /// <param name="audioPath">Path to preprocessed audio.</param>
    /// <param name="numSpeakers">Expected number of speakers; 0 selects automatically.</param>
    /// <param name="segmentModel">Segmentation model name; null uses the backend default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Speaker segments and usage statistics.</returns>
    Task<ProviderResult<IReadOnlyList<Centurion.Abstractions.Strategy.SpeakerSegment>>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken);
}

/// <summary>
/// Vocal separation provider that extracts a vocal track from audio.
/// </summary>
public interface IVocalSeparationProvider : IProvider
{
    /// <summary>
    /// Separates the vocal track.
    /// </summary>
    /// <param name="audioPath">Input audio path.</param>
    /// <param name="outputWavPath">Output vocal WAV path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Actual vocal track path and usage statistics.</returns>
    Task<ProviderResult<string>> SeparateVocalsAsync(
        string audioPath,
        string outputWavPath,
        CancellationToken cancellationToken);
}
