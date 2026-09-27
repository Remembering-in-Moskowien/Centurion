using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.Diarization;using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// 说话人分割策略工厂：按后端名称分发到对应实现。
/// </summary>
public class DiarizationStrategyFactory(IServiceProvider serviceProvider) : IDiarizationStrategyFactory
{
    /// <summary>
    /// 按后端名称创建说话人分割策略。
    /// </summary>
    /// <param name="backend">说话人分割后端名称，支持 "crispasr" 或 "pyannote"。</param>
    /// <returns>对应后端的说话人分割策略实例。</returns>
    /// <exception cref="NotSupportedException">当后端名称不受支持时抛出。</exception>
    public IDiarizationStrategy Create(string backend)
    {
        return backend.ToLowerInvariant() switch
        {
            "crispasr" => serviceProvider.GetRequiredService<CrispAsrDiarizationStrategy>(),
            "pyannote" => serviceProvider.GetRequiredService<PyannoteTitaNetDiarizationStrategy>(),
            _ => throw new NotSupportedException($"Diarization backend '{backend}' is not supported. Use 'crispasr' or 'pyannote'.")
        };
    }
}
