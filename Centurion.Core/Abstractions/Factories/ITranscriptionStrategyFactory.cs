using Centurion.Core.Abstractions.Strategy;

namespace Centurion.Core.Abstractions.Factories;

/// <summary>
/// 转录策略工厂
/// </summary>
public interface ITranscriptionStrategyFactory
{
    /// <param name="engine">引擎名称（如 whisper, qwen, api）</param>
    /// <param name="model">模型名称（可选，如 base, large）</param>
    /// <param name="language">语言代码</param>
    /// <param name="initialPrompt">初始提示（可选）</param>
    ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt);
}
