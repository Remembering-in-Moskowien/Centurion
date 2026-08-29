using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Exceptions;
using Centurion.Core.Models;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 强制对齐算子，通过工厂动态选择对齐引擎（Qwen/Gentle 等）。
/// </summary>
public class AlignmentOperator : PipelineOperatorBase, IHealthCheckableOperator
{
    private readonly IAlignmentStrategyFactory _factory;

    public override string Name => "Forced Alignment";

    public AlignmentOperator(IAlignmentStrategyFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // 对齐策略的健康检查可在此委托，也可留空
        await Task.CompletedTask;
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 检查点
        if (context.State.IsAligned)
        {
            LogInfo("Alignment already exists, skipping.");
            return;
        }

        var config = context.Config;

        // 若未启用对齐，直接标记完成
        if (string.IsNullOrEmpty(config.AlignerEngine))
        {
            LogInfo("Aligner not enabled. Marking as aligned (no-op).");
            context.State.AlignedSentences = new List<Sentence>();
            context.State.IsAligned = true;
            return;
        }

        // 输入：优先使用分句后的结果，否则使用转录结果
        var inputSentences = context.State.SplitSentences?.Count > 0
            ? context.State.SplitSentences
            : context.State.WhisperSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            LogWarning("No sentences to align. Marking as aligned (empty).");
            context.State.AlignedSentences = new List<Sentence>();
            context.State.IsAligned = true;
            return;
        }

        // 音频路径
        var audioPath = context.State.ConvertedAudioPath ?? config.InputFilePath;
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 通过工厂创建对齐策略
        var strategy = _factory.Create(config.AlignerEngine, config.AlignerModel);
        LogInfo($"Using alignment engine: {strategy.GetType().Name}");

        try
        {
            OnProgress(10, $"Starting alignment...");
            var aligned = await strategy.AlignAsync(inputSentences, audioPath, cancellationToken);

            context.State.AlignedSentences = aligned ?? new List<Sentence>();
            context.State.IsAligned = true;

            OnProgress(100, $"Alignment completed. {context.State.AlignedSentences.Count} sentences.");
            LogInfo($"Aligned {context.State.AlignedSentences.Count} sentences.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError($"Alignment failed: {ex.Message}");
            throw new AlignmentException($"Alignment failed: {ex.Message}", ex);
        }
    }
}