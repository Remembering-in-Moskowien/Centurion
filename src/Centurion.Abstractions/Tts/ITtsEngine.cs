using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Abstractions.Tts;

/// <summary>
/// Text-to-speech engine abstraction that synthesizes a WAV segment from target text, reference audio, and language.
/// Implementations may use a local CLI such as llama-tts or a remote API; pipeline operators are backend-agnostic.
/// </summary>
public interface ITtsEngine
{
    /// <summary>Engine name, such as "llama".</summary>
    string EngineName { get; }

    /// <summary>
    /// Synthesizes one utterance.
    /// </summary>
    /// <param name="text">Target-language text with excess whitespace removed.</param>
    /// <param name="referenceAudioPath">Speaker reference audio path; null uses the engine's default voice.</param>
    /// <param name="language">Target language (ISO 639-1, such as zh).</param>
    /// <param name="outputWavPath">Output WAV file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Synthesized audio duration in seconds; throws TtsSynthesisException on failure.</returns>
    Task<double> SynthesizeAsync(string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken);
}

/// <summary>Exception thrown when TTS synthesis fails; callers can log the cause as a warning and skip the sentence.</summary>
public sealed class TtsSynthesisException(string message, Exception? inner = null) : Exception(message, inner);
