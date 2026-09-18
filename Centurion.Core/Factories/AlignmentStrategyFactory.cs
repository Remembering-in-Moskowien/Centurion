using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.Alignment;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

public class AlignmentStrategyFactory(IServiceProvider serviceProvider) : IAlignmentStrategyFactory
{
    public IAlignmentStrategy Create(string modelName)
    {
        // Use ActivatorUtilities to resolve the strategy with runtime modelName
        return ActivatorUtilities.CreateInstance<CrispAsrAlignmentStrategy>(
            serviceProvider, modelName);
    }
}
