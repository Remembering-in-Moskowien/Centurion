using Centurion.Abstractions;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Capabilities.Managers.Media;
namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// 按模型类别从注册表解析并按需下载模型，返回各后端模型文件的本地路径。
/// </summary>
public class ModelPathResolver(IServiceProvider serviceProvider, ModelRegistry modelRegistry) : IModelPathResolver
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ModelRegistry _modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));

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

    /// <summary>
    /// 确保指定的 whisper.cpp 模型就绪并返回其本地文件路径。
    /// </summary>
    /// <param name="modelName">模型名称。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>模型文件的本地路径。</returns>
    public async Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.WhisperModels, "whispercpp");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// 确保指定的 faster-whisper 模型就绪并返回其本地文件路径。
    /// </summary>
    /// <param name="modelName">模型名称。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>模型文件的本地路径。</returns>
    public async Task<string> GetFasterWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.FasterWhisperModels, "fasterwhisper");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// 确保指定的 Qwen3 ASR 模型就绪并返回其本地文件路径。
    /// </summary>
    /// <param name="modelName">模型名称。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>模型文件的本地路径。</returns>
    public async Task<string> GetQwen3AsrModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.Qwen3AsrModels, "qwen3asr");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// 确保指定的说话人分割（diarization）模型就绪并返回其本地文件路径。
    /// </summary>
    /// <param name="modelName">模型名称。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>模型文件的本地路径。</returns>
    public async Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.DiarizationModels, "diarization");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <summary>
    /// 确保指定的 Qwen3 强制对齐模型就绪并返回其本地文件路径。
    /// </summary>
    /// <param name="modelName">模型名称。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>模型文件的本地路径。</returns>
    public async Task<string> GetQwen3ForcedAlignerPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var manager = CreateManager(modelName, _modelRegistry.Qwen3ForcedAlignerModels, "qwen3aligner");
        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }
}
