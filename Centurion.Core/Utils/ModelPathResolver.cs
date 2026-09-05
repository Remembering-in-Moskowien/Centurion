// Centurion.Core/Utils/ModelPathResolver.cs
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Utils;

public class ModelPathResolver(IServiceProvider serviceProvider) : IModelPathResolver
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    // 私有工厂方法，用于创建 ModelManager 实例
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

    public async Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, ModelRegistry.WhisperModels, "whispercpp");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    public async Task<string> GetFasterWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, ModelRegistry.FasterWhisperModels, "fasterwhisper");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    public async Task<string> GetQwen3AsrModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, ModelRegistry.Qwen3AsrModels, "qwen3asr");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    public async Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, ModelRegistry.DiarizationModels, "diarization");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }
    
    public async Task<string> GetQwen3ForcedAlignerPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, ModelRegistry.Qwen3ForcedAlignerModels, "qwen3aligner");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }
}