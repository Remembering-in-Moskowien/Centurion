using Centurion.Abstractions.Strategy;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// 转录策略工厂
/// </summary>
public interface ITranscriptionStrategyFactory
{
    /// <param name="engine">引擎名称（如 whisper, qwen, api）</param>
    /// <param name="model">模型名称（可选，如 base, large）</param>
    /// <param name="language">语言代码</param>
    /// <param name="initialPrompt">初始提示（可选）</param>
    /// <param name="asrOptions">云端 ASR 连接配置；本地引擎忽略此参数。</param>
    ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt, AsrOptions? asrOptions = null);
}
