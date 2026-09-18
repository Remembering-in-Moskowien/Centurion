using Centurion.Core.Abstractions.Strategy;

namespace Centurion.Core.Abstractions.Factories;

/// <summary>
/// Factory for creating alignment strategy instances based on model name.
/// </summary>
public interface IAlignmentStrategyFactory
{
    /// <summary>
    /// Creates an alignment strategy for the specified model.
    /// </summary>
    /// <param name="modelName">Name of the alignment model (e.g., wav2vec2-base-960h).</param>
    /// <returns>An instance of IAlignmentStrategy.</returns>
    IAlignmentStrategy Create(string modelName);
}
