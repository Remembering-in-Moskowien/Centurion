using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Utils.Audio;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// VAD 过滤算子：检测语音段、剔除器乐/静音段并聚合为连续语音音频。
/// 优先使用 Silero VAD（ONNX，区分语音与纯器乐/静音）；模型缺失时自动降级为
/// 能量型 VAD（剔除静音/低能量段）并记录警告。产物（聚合音频 + 段映射）供
/// 人声分离/转录优先消费——分离只处理语音内容，避免器乐/杂音干扰 Demucs
/// 分离质量；转录词时间再按映射还原回源时间轴。
/// </summary>
public sealed class VoiceActivityFilterOperator(
    ILogger<VoiceActivityFilterOperator> logger,
    SileroVadModelManager sileroModelManager) : PipelineOperatorBase<VoiceActivityFilterOperator>(logger)
{
    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Voice Activity Filter";

    /// <summary>
    /// 执行 VAD：读取输入音频样本、检测语音段（Silero 优先）、聚合写出聚合音频，
    /// 并把段列表（含聚合位置）写入工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供配置、状态与输入音频路径。</param>
    /// <param name="cancellationToken">用于取消检测的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. 开关
        if (!config.EnableVadFilter)
        {
            LogInfo("VAD filter disabled (EnableVadFilter = false).");
            return;
        }

        // 2. 输入音频（人声分离之前：预处理/转换后的源音频）
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for VAD filter: {inputPath}");
            return;
        }
        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping VAD filter.");
            return;
        }
        Directory.CreateDirectory(tempDir);

        // 3. 读样本
        var (samples, sampleRate) = WavSampleReader.ReadMono(inputPath);
        if (samples.Length == 0)
        {
            LogWarning($"No audio samples read from {inputPath}; skipping VAD filter.");
            return;
        }

        // 4. 检测：Silero 优先，能量 VAD 降级
        List<VoiceSegment> segments;
        var options = new VadDetector.VadOptions(
            WindowSeconds: config.VadWindowSeconds,
            MergeGapSeconds: config.VadMergeGapSeconds,
            MinSpeechSeconds: config.VadMinSpeechSeconds,
            EnergyThresholdRatio: config.VadEnergyThresholdRatio,
            AbsoluteFloorDb: config.VadAbsoluteFloorDb);

        var modelPath = await sileroModelManager.EnsureModelAsync(cancellationToken);
        if (modelPath is not null && sampleRate == 16000)
        {
            try
            {
                using var silero = new SileroVadDetector(modelPath);
                segments = silero.Detect(samples, sampleRate, options);
                if (silero.SpeechProbabilities is { Length: > 0 } probs)
                {
                    var sorted = probs.OrderBy(x => x).ToArray();
                    LogInfo($"Silero VAD probabilities: min={sorted[0]:F3} p50={sorted[sorted.Length / 2]:F3} " +
                            $"p90={sorted[(int)(sorted.Length * 0.9)]:F3} max={sorted[^1]:F3} frames={probs.Length} " +
                            $"(>0.5×{probs.Count(x => x > 0.5f)}, >0.3×{probs.Count(x => x > 0.3f)}, >0.2×{probs.Count(x => x > 0.2f)})");
                }
                LogInfo($"Silero VAD: {segments.Count} speech segment(s) detected.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarning($"Silero VAD inference failed ({ex.Message}); falling back to the energy-based VAD.");
                segments = VadDetector.Detect(samples, sampleRate, options);
            }
        }
        else
        {
            if (sampleRate != 16000)
                LogWarning($"Input sample rate {sampleRate} Hz is not 16 kHz; using the energy-based VAD.");
            segments = VadDetector.Detect(samples, sampleRate, options);
        }

        if (segments.Count == 0)
        {
            LogWarning("VAD detected no speech segments; continuing with the full source audio.");
            return;
        }

        // 5. 聚合：抽取语音段拼接（段间 0.1s 静音缓冲），回填聚合位置
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
    }
}
