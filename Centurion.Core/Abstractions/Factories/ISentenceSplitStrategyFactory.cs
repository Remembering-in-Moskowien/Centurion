using Centurion.Core.Abstractions.Strategy;

namespace Centurion.Core.Abstractions.Factories;

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
