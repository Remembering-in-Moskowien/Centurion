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
    /// <summary>
    /// 按语音识别引擎名称创建对应的转录策略。
    /// </summary>
    /// <param name="engine">转录引擎名称，如 "whispercpp"、"crispasr"/"crispasr-qwen"、"crispasr-whisper"。</param>
    /// <param name="model">可选的模型名称，供指定模型路径或版本时使用。</param>
    /// <param name="language">目标语言代码。</param>
    /// <param name="initialPrompt">可选的初始提示词，用于引导转录风格或上下文。</param>
    /// <returns>对应引擎的转录策略实例。</returns>
    /// <exception cref="NotSupportedException">当引擎名称不受支持时抛出。</exception>
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
