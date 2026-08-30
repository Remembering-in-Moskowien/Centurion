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

/// <summary>
/// 分句策略工厂
/// </summary>
public interface ISentenceSplitStrategyFactory
{
    /// <param name="strategy">策略名称（如 heuristic, llm, rule）</param>
    /// <param name="options">分句配置参数</param>
    /// <param name="model"></param>
    /// <param name="apiKey"></param>
    ISentenceSplitStrategy Create(string strategy, SplitOptions options, string? model = null, string? apiKey = null);
}