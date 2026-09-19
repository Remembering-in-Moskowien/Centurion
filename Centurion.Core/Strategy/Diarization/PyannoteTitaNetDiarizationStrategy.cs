using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Strategy.Diarization;

/// <summary>
/// 方案二：Pyannote 分割 + TitaNet 嵌入（均通过 CrispASR CLI 原生实现，无需 Python）。
/// --diarize-method pyannote（原生 GGUF 分割）+ --diarize-embedder auto（TitaNet 说话人嵌入，
/// 由 CrispASR 自动下载），为长音频提供全局稳定的说话人 ID。
/// </summary>
public sealed class PyannoteTitaNetDiarizationStrategy(IServiceProvider serviceProvider)
    : CrispAsrDiarizationBase(serviceProvider)
{
    /// <summary>策略的显示名称。</summary>
    public override string StrategyName => "Pyannote + TitaNet Diarization";

    /// <summary>传给 --diarize-method 的方法名，固定为 pyannote。</summary>
    protected override string DiarizeMethod => "pyannote";

    /// <summary>传给 --diarize-embedder 的嵌入器，固定为 auto（自动下载 TitaNet GGUF）。</summary>
    protected override string? DiarizeEmbedder => "auto";

    /// <summary>pyannote 默认分割模型名（可由 DiarizationModel 配置覆盖）。</summary>
    protected override string? DefaultSegmentModel => "pyannote-seg-3.0";
}
