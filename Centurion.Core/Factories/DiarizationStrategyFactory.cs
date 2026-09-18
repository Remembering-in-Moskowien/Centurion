using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Strategy.Diarization;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

/// <summary>
/// 说话人分割策略工厂：按后端名称分发到对应实现。
/// </summary>
public class DiarizationStrategyFactory(IServiceProvider serviceProvider) : IDiarizationStrategyFactory
{
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
