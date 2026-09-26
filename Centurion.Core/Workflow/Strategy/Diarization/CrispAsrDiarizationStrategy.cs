using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// 方案一：CrispASR（已有工具）内置说话人分割方法。
/// 支持 energy / xcorr / vad-turns / foxnose（默认 foxnose：精度最高、无需立体声、自动估计说话人数）。
/// </summary>
public sealed class CrispAsrDiarizationStrategy(IServiceProvider serviceProvider)
    : CrispAsrDiarizationBase(serviceProvider)
{
    /// <summary>策略的显示名称。</summary>
    public override string StrategyName => "CrispASR Diarization";

    /// <summary>默认使用 foxnose（WeSpeaker 嵌入 + 谱聚类，无外部依赖）。</summary>
    public string Method { get; set; } = "foxnose";

    /// <summary>传给 --diarize-method 的方法名，取可配置的 <see cref="Method"/>。</summary>
    protected override string DiarizeMethod => Method;
}
