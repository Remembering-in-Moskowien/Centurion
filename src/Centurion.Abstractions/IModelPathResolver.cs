namespace Centurion.Abstractions;

/// <summary>
/// Resolves local model paths by model name and starts a download when the model is missing.
/// </summary>
public interface IModelPathResolver
{
    /// <summary>
    /// Gets the path to a Whisper.cpp model .bin file.
    /// </summary>
    /// <param name="modelName">Model name: tiny, base, small, medium, or large.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full path to the .bin file.</returns>
    Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the directory path for a Faster-Whisper model.
    /// </summary>
    /// <param name="modelName">Model name: tiny, base, small, medium, or large-v3.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model directory path.</returns>
    Task<string> GetFasterWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to a Qwen3-ASR.cpp model .gguf file.
    /// </summary>
    /// <param name="modelName">Model name, such as qwen3-asr-0.6b.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full path to the .gguf file.</returns>
    Task<string> GetQwen3AsrModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the directory path for a forced-alignment model.
    /// </summary>
    Task<string> GetQwen3ForcedAlignerPathAsync(string modelName, CancellationToken cancellationToken = default);
}