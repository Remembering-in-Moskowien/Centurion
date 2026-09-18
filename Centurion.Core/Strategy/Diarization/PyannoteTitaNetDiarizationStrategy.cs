// Centurion.Core/Strategy/Diarization/PyannoteTitaNetDiarizationStrategy.cs

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
    public override string StrategyName => "Pyannote + TitaNet Diarization";

    protected override string DiarizeMethod => "pyannote";

    // TitaNet 嵌入器（"auto" = 自动下载 TitaNet GGUF）
    protected override string? DiarizeEmbedder => "auto";

    // pyannote 分割模型（可由 DiarizationModel 配置覆盖）
    protected override string? DefaultSegmentModel => "pyannote-seg-3.0";
}
