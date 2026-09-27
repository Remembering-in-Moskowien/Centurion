using Centurion.Abstractions.Strategy;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// 说话人分割策略工厂：按后端名称创建对应的分割策略。
/// </summary>
public interface IDiarizationStrategyFactory
{
    /// <summary>
    /// 创建指定后端的说话人分割策略。
    /// </summary>
    /// <param name="backend">"crispasr" 或 "pyannote"</param>
    /// <returns>说话人分割策略实例</returns>
    IDiarizationStrategy Create(string backend);
}
