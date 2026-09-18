using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.Transcribe;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

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
    public ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt)
    {
        var engineLower = engine.ToLowerInvariant();

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
