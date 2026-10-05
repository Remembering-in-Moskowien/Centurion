using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.Alignment;using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Alignment strategy factory: resolves and creates the matching text-alignment strategy instance by model name.
/// </summary>
public class AlignmentStrategyFactory(IServiceProvider serviceProvider) : IAlignmentStrategyFactory
{
    /// <summary>
    /// Creates the text-alignment strategy for the given model name.
    /// </summary>
    /// <param name="modelName">The model name used for alignment; injected into the strategy instance at runtime.</param>
    /// <returns>An alignment strategy instance with the model name resolved.</returns>
    public IAlignmentStrategy Create(string modelName)
    {
        // Use ActivatorUtilities to resolve the strategy with runtime modelName
        return ActivatorUtilities.CreateInstance<CrispAsrAlignmentStrategy>(
            serviceProvider, modelName);
    }
}
