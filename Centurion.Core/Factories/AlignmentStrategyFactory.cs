using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.Alignment;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

/// <summary>
/// 对齐策略工厂：按模型名称解析并创建对应的文本对齐策略实例。
/// </summary>
public class AlignmentStrategyFactory(IServiceProvider serviceProvider) : IAlignmentStrategyFactory
{
    /// <summary>
    /// 按模型名称创建文本对齐策略。
    /// </summary>
    /// <param name="modelName">对齐所用的模型名称，将在运行时注入到策略实例中。</param>
    /// <returns>已解析好模型名称的对齐策略实例。</returns>
    public IAlignmentStrategy Create(string modelName)
    {
        // Use ActivatorUtilities to resolve the strategy with runtime modelName
        return ActivatorUtilities.CreateInstance<CrispAsrAlignmentStrategy>(
            serviceProvider, modelName);
    }
}
