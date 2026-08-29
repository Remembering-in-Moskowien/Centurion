using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Models.Metadata;

namespace Centurion.Core.Utils;

/// <summary>
/// Implementation of <see cref="IModelPathResolver"/> that uses <see cref="ModelManager"/>
/// to resolve and ensure model files are available locally.
/// </summary>
public class ModelPathResolver : IModelPathResolver
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelPathResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider used to instantiate <see cref="ModelManager"/>.</param>
    public ModelPathResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public async Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        // Whisper models are directory-based (FasterWhisper)
        var manager = new ModelManager(
            modelName,
            ModelRegistry.FasterWhisperModels,
            _serviceProvider,
            categoryFolder: "whisper");

        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <inheritdoc />
    public async Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        // Diarization models are single-file ONNX
        var manager = new ModelManager(
            modelName,
            ModelRegistry.DiarizationModels,
            _serviceProvider,
            categoryFolder: "diarization");

        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }

    /// <inheritdoc />
    public async Task<string> GetAlignmentModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        // Alignment models are directory-based (containing model.onnx and tokens.txt)
        var manager = new ModelManager(
            modelName,
            ModelRegistry.Wav2Vec2Models,
            _serviceProvider,
            categoryFolder: "alignment");

        await manager.CheckHealthAsync();
        return manager.ModelFilePath;
    }
}