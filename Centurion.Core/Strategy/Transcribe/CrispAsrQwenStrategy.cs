using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Transcribe;

/// <summary>
/// 基于 CrispASR Qwen3 后端的转录策略：使用 qwen3-asr 模型转录，并附带 Qwen3 强制对齐器。
/// </summary>
public class CrispAsrQwenStrategy : CrispAsrBaseStrategy
{
    /// <summary>策略的显示名称。</summary>
    public override string StrategyName => "CrispASR (Qwen3)";

    private const string DefaultAlignerModel = "qwen3-forced-aligner-0.6b";

    /// <summary>创建 Qwen3 转录策略实例。</summary>
    /// <param name="serviceProvider">用于解析依赖服务的容器。</param>
    public CrispAsrQwenStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    /// <summary>传给 CrispASR 的后端名，固定为 qwen3。</summary>
    protected override string GetBackendName() => "qwen3";

    /// <summary>解析 Qwen3 ASR 模型的本地路径；模型名为空时回退到默认模型。</summary>
    /// <param name="modelName">请求的模型名。</param>
    /// <param name="cancellationToken">用于取消路径解析的取消标记。</param>
    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "qwen3-asr-0.6b";
        return await _modelResolver.GetQwen3AsrModelPathAsync(modelName, cancellationToken);
    }

    /// <summary>解析 Qwen3 强制对齐器的本地路径；解析失败时返回 null 以跳过对齐。</summary>
    /// <param name="cancellationToken">用于取消路径解析的取消标记。</param>
    protected override async Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            var alignerPath = await _modelResolver.GetQwen3ForcedAlignerPathAsync(DefaultAlignerModel, cancellationToken);
            return alignerPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Qwen3 aligner model. Alignment disabled.");
            return null;
        }
    }
}