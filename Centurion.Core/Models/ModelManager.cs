using Centurion.Core.Exceptions;
using Centurion.Core.Operators;
using Centurion.Core.Operators.Payload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Models;

/// <summary>
/// 模型管理器，负责模型文件的下载、校验和路径管理。
/// 可独立使用，无需继承。
/// </summary>
public class ModelManager : IDisposable
{
    private readonly IStringLocalizer<Localization> _localizer;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="modelName">模型名称（对应 ModelDict 中的键）</param>
    /// <param name="modelDict">模型元数据字典（由调用方提供）</param>
    /// <param name="localizer"></param>
    /// <param name="serviceProvider"></param>
    /// <param name="categoryFolder">模型分类文件夹名（如 whisper/diarization/vad）</param>
    /// <exception cref="ArgumentException">当 modelName 不在 modelDict 中时抛出</exception>
    public ModelManager(string modelName,
        IReadOnlyDictionary<string, ModelMeta> modelDict,
        IStringLocalizer<Localization> localizer,
        IServiceProvider serviceProvider,
        string categoryFolder = "common")
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _serviceProvider = serviceProvider;
        _modelName = string.Empty;
        TargetMeta = null;
        ModelFolder = string.Empty;
        ModelFilePath = string.Empty;

        if (string.IsNullOrEmpty(modelName))
        {
            ManagementEnabled = false;
            return;
        }

        ManagementEnabled = true;
        _modelName = modelName.Trim().ToLowerInvariant();
        if (!modelDict.TryGetValue(_modelName, out var tempMeta))
            throw new ArgumentException(
                _localizer["UnsupportedModel", _modelName],
                nameof(modelName));

        TargetMeta = tempMeta;
        ModelFolder = Path.Combine(AppContext.BaseDirectory, "models", categoryFolder);
        ModelFilePath = Path.Combine(ModelFolder, TargetMeta!.FileName);
    }

    public string ModelFilePath { get; }

    public string ModelFolder { get; }

    public ModelMeta? TargetMeta { get; }

    public bool ManagementEnabled { get; }

    /// <summary>
    /// 确保模型文件存在并校验完整性
    /// </summary>
    public async Task EnsureModelAvailableAsync()
    {
        if (!ManagementEnabled) return;
        if (!File.Exists(ModelFilePath)) await DownloadModelAsync();
        VerifyModelHash();
    }

    private void VerifyModelHash()
    {
        if (!ManagementEnabled) return;
        var hashResult = SubTools.VerifyHash(ModelFilePath, TargetMeta!.Sha256Hash);
        if (!hashResult.IsMatch)
            throw new FileHashMismatchException(
                _localizer["FileHashNotMatch"],
                ModelFilePath,
                TargetMeta!.Sha256Hash,
                hashResult.ActualHash);
    }

    private async Task DownloadModelAsync()
    {
        if (!ManagementEnabled) return;
        Directory.CreateDirectory(ModelFolder);
        ConsoleServices.Output.WriteLine(_localizer["ModelNotFound", _modelName]);
        if (!await ConsoleServices.Confirm.ConfirmAsync(_localizer["ConfirmContinueInstallation"]))
            throw new InvalidOperationException(_localizer["FileHashNotMatch"]);

        using var aria = _serviceProvider.GetRequiredService<AriaOperator>();
        var dlRequest = new OperatorsRequest<AriaDownloadPayload>
        {
            Payload = new AriaDownloadPayload
            {
                Url = TargetMeta!.DownloadUrl,
                FullSavePath = ModelFilePath,
                FileHash = TargetMeta!.Sha256Hash
            }
        };
        await aria.SendRequestAsync<object, AriaDownloadPayload>(dlRequest);
    }

    public void Dispose()
    {
        // 无需要释放的资源（AriaOperator 已在 using 中释放）
    }
}