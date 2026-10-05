using Centurion.Abstractions.Strategy;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Factory that creates transcription strategies.
/// </summary>
public interface ITranscriptionStrategyFactory
{
    /// <param name="engine">Engine name, such as whisper, qwen, or api.</param>
    /// <param name="model">Optional model name, such as base or large.</param>
    /// <param name="language">Language code.</param>
    /// <param name="initialPrompt">Optional initial prompt.</param>
    /// <param name="asrOptions">Cloud ASR connection options; ignored by local engines.</param>
    ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt, AsrOptions? asrOptions = null);
}
