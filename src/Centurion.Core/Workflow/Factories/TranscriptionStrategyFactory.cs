using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Infrastructure.Asr;using Centurion.Core.Workflow.Strategy.Transcribe;using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Transcription strategy factory supporting multiple backends:
/// - whispercpp (Whisper.cpp CLI)
/// - crispasr-qwen (CrispASR with Qwen3)
/// - crispasr-whisper (CrispASR with Whisper)
/// - crispasr (alias for qwen)
/// - whisper (legacy, maps to whispercpp)
/// </summary>
public class TranscriptionStrategyFactory(IServiceProvider serviceProvider) : ITranscriptionStrategyFactory
{
    /// <summary>
    /// Creates the transcription strategy matching the given speech-recognition engine name.
    /// </summary>
    /// <param name="engine">The transcription engine name, e.g. "whispercpp", "crispasr"/"crispasr-qwen", "crispasr-whisper".</param>
    /// <param name="model">Optional model name, used to specify a model path or version.</param>
    /// <param name="language">The target language code.</param>
    /// <param name="initialPrompt">Optional initial prompt used to guide the transcription style or context.</param>
    /// <param name="asrOptions">Cloud ASR connection options (provider key/endpoint); ignored by local engines.</param>
    /// <returns>The transcription strategy instance for the given engine.</returns>
    /// <exception cref="NotSupportedException">Thrown when the engine name is not supported.</exception>
    public ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt, AsrOptions? asrOptions = null)
    {
        var engineLower = engine.ToLowerInvariant();

        // Cloud ASR API strategy
        var cloud = AsrEndpointParser.Resolve(engineLower);
        if (cloud is not null)
        {
            var strategy = serviceProvider.GetRequiredService<CloudAsrStrategy>();
            strategy.Provider = cloud.Value.Provider;
            strategy.ApiKey = asrOptions?.ApiKey;
            strategy.BaseUrl = asrOptions?.BaseUrl;
            return strategy;
        }

        return engineLower switch
        {
            // Whisper.cpp via external CLI
            "whispercpp" or "whisper.cpp" or "whisper-cpp" or "whisper-cli" or "whisper"
                => serviceProvider.GetRequiredService<WhisperCppStrategy>(),

            // CrispASR with Qwen3 backend (default)
            "crispasr" or "crisp" or "crispasr-qwen" or "crisp-qwen"
                => serviceProvider.GetRequiredService<CrispAsrQwenStrategy>(),

            // CrispASR with Whisper backend
            "crispasr-whisper" or "crisp-whisper"
                => serviceProvider.GetRequiredService<CrispAsrWhisperStrategy>(),

            // future extensions
            // "api" => serviceProvider.GetRequiredService<ApiTranscriptionStrategy>(),

            _ => throw new NotSupportedException($"Transcription engine '{engine}' is not supported.")
        };
    }
}
