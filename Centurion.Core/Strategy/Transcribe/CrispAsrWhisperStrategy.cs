namespace Centurion.Core.Strategy.Transcribe;

/// <summary>
/// 基于 CrispASR Whisper 后端的转录策略：使用 Whisper 模型转录，不附带强制对齐器。
/// </summary>
public class CrispAsrWhisperStrategy : CrispAsrBaseStrategy
{
    /// <summary>策略的显示名称。</summary>
    public override string StrategyName => "CrispASR (Whisper)";

    /// <summary>创建 Whisper 转录策略实例。</summary>
    /// <param name="serviceProvider">用于解析依赖服务的容器。</param>
    public CrispAsrWhisperStrategy(IServiceProvider serviceProvider) : base(serviceProvider) { }

    /// <summary>传给 CrispASR 的后端名，固定为 whisper。</summary>
    protected override string GetBackendName() => "whisper";

    /// <summary>解析 Whisper 模型的本地路径；模型名为空时回退到默认模型。</summary>
    /// <param name="modelName">请求的模型名。</param>
    /// <param name="cancellationToken">用于取消路径解析的取消标记。</param>
    protected override async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken)
    {
        // Use default if modelName is empty
        if (string.IsNullOrEmpty(modelName))
            modelName = "base";
        return await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
    }

    /// <summary>Whisper 后端不使用对齐器，固定返回 null。</summary>
    /// <param name="cancellationToken">取消标记（本实现不使用）。</param>
    protected override Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}