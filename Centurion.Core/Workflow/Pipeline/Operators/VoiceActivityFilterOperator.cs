using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Centurion.Core.Utils.Audio;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// VAD 过滤算子：检测语音段、剔除器乐/静音段并聚合为连续语音音频。
/// 产物（聚合音频 + 段映射）供人声分离/转录优先消费——分离只处理语音内容，
/// 避免器乐/杂音干扰 Demucs 分离质量；转录词时间再按映射还原回源时间轴。
/// 仅在 WorkflowConfig.EnableVadFilter 开启时执行；开启时始终产出聚合产物。
/// </summary>
public sealed class VoiceActivityFilterOperator(
    ILogger<VoiceActivityFilterOperator> logger) : PipelineOperatorBase<VoiceActivityFilterOperator>(logger)
{
    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Voice Activity Filter";

    /// <summary>
    /// 执行 VAD：读取输入音频样本、检测语音段、聚合写出聚合音频，
    /// 并把段列表（含聚合位置）写入工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供配置、状态与输入音频路径。</param>
    /// <param name="cancellationToken">用于取消检测的取消标记。</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. 开关
        if (!config.EnableVadFilter)
        {
            LogInfo("VAD filter disabled (EnableVadFilter = false).");
            return Task.CompletedTask;
        }

        // 2. 输入音频（人声分离之前：预处理/转换后的源音频）
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for VAD filter: {inputPath}");
            return Task.CompletedTask;
        }
        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping VAD filter.");
            return Task.CompletedTask;
        }
        Directory.CreateDirectory(tempDir);

        // 3. 读样本 → 检测 → 聚合
        var (samples, sampleRate) = WavSampleReader.ReadMono(inputPath);
        if (samples.Length == 0)
        {
            LogWarning($"No audio samples read from {inputPath}; skipping VAD filter.");
            return Task.CompletedTask;
        }

        var options = new VadDetector.VadOptions(
            WindowSeconds: config.VadWindowSeconds,
            MergeGapSeconds: config.VadMergeGapSeconds,
            MinSpeechSeconds: config.VadMinSpeechSeconds,
            EnergyThresholdRatio: config.VadEnergyThresholdRatio,
            AbsoluteFloorDb: config.VadAbsoluteFloorDb);

        var segments = VadDetector.Detect(samples, sampleRate, options);
        if (segments.Count == 0)
        {
            LogWarning("VAD detected no speech segments; continuing with the full source audio.");
            return Task.CompletedTask;
        }

        var outputPath = Path.Combine(tempDir, $"vad_agg_{Guid.NewGuid():N}.wav");
        var mapped = VadAggregator.Aggregate(samples, sampleRate, segments, outputPath, config.VadPadSeconds);

        var sourceSeconds = sampleRate > 0 ? samples.Length / (double)sampleRate : 0;
        var speechSeconds = mapped.Sum(s => (s.EndMs - s.StartMs) / 1000.0);
        context.State.VoiceSegments = [.. mapped];
        context.State.VadAggregatedPath = outputPath;
        context.State.VadRemovedSeconds = Math.Max(0, sourceSeconds - speechSeconds);

        OnProgress(100, "Voice activity filter completed.");
        LogInfo($"VAD: {mapped.Count} speech segment(s), " +
                $"{speechSeconds:F1}s speech aggregated, " +
                $"{context.State.VadRemovedSeconds:F1}s instrumental/silence removed. Aggregated: {outputPath}");
        return Task.CompletedTask;
    }
}
