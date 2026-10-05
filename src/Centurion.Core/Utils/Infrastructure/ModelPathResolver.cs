using Centurion.Abstractions;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Capabilities.Managers.Media;
namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// Resolves models from the registry by category and downloads them on demand, returning the local paths of the model files for each backend.
/// </summary>
public class ModelPathResolver(IServiceProvider serviceProvider, ModelRegistry modelRegistry) : IModelPathResolver
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ModelRegistry _modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));

    // Private factory method used to create ModelManager instances
    private ModelManager CreateManager(
        string modelName,
        IReadOnlyDictionary<string, ModelMeta> modelDict,
        string categoryFolder)
    {
        return ActivatorUtilities.CreateInstance<ModelManager>(
            _serviceProvider,
            modelName,
            modelDict,
            categoryFolder);
    }

    /// <summary>
    /// Ensures the given whisper.cpp model is ready and returns its local file path.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The local path of the model file.</returns>
    public async Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.WhisperModels, "whispercpp");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// Ensures the given faster-whisper model is ready and returns its local file path.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The local path of the model file.</returns>
    public async Task<string> GetFasterWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.FasterWhisperModels, "fasterwhisper");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// Ensures the given Qwen3 ASR model is ready and returns its local file path.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The local path of the model file.</returns>
    public async Task<string> GetQwen3AsrModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.Qwen3AsrModels, "qwen3asr");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// Ensures the given speaker diarization model is ready and returns its local file path.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The local path of the model file.</returns>
    public async Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.DiarizationModels, "diarization");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// Ensures the given Qwen3 forced-alignment model is ready and returns its local file path.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The local path of the model file.</returns>
    public async Task<string> GetQwen3ForcedAlignerPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.Qwen3ForcedAlignerModels, "qwen3aligner");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }
}
