using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Audio;

/// <summary>
/// 能量型语音活动检测（VAD）：按窗口 RMS 判定语音段，剔除器乐/静音段。
/// 阈值取「全局 RMS 均值 × 系数」与「绝对下限」的较大者（自适应，不依赖绝对电平）；
/// 检出段支持合并（间隙小于阈值）与最短时长过滤（剔除能量毛刺）。
/// 纯算法实现，便于单元测试；配合 <see cref="VadAggregator"/> 完成聚合。
/// </summary>
public static class VadDetector
{
    /// <summary>VAD 检测参数。</summary>
    /// <param name="WindowSeconds">能量窗口时长（秒），默认 0.5。</param>
    /// <param name="MergeGapSeconds">相邻语音段间隔小于该秒数时合并，默认 1.0。</param>
    /// <param name="MinSpeechSeconds">短于该秒数的语音段丢弃，默认 0.3。</param>
    /// <param name="EnergyThresholdRatio">阈值系数：窗口 RMS 低于 全局均值×系数 视为无语音，默认 0.2。</param>
    /// <param name="AbsoluteFloorDb">绝对能量下限（dBFS），低于视为静音，默认 -50。</param>
    public readonly record struct VadOptions(
        double WindowSeconds = 0.5,
        double MergeGapSeconds = 1.0,
        double MinSpeechSeconds = 0.3,
        double EnergyThresholdRatio = 0.2,
        double AbsoluteFloorDb = -50);

    /// <summary>
    /// 检测语音段。
    /// </summary>
    /// <param name="samples">单声道样本（-1..1）。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <param name="options">检测参数。</param>
    /// <returns>语音段列表（源时间轴，毫秒；含段平均 RMS dB）。</returns>
    public static List<VoiceSegment> Detect(float[] samples, int sampleRate, VadOptions options)
    {
        var result = new List<VoiceSegment>();
        if (samples.Length == 0 || sampleRate <= 0)
            return result;

        var win = Math.Max(1, (int)Math.Round(options.WindowSeconds * sampleRate));
        var windowCount = samples.Length / win;
        if (windowCount == 0)
            return result;

        // 1) 逐窗口 RMS（线性）
        var rms = new double[windowCount];
        var sum = 0.0;
        for (var i = 0; i < windowCount; i++)
        {
            var off = i * win;
            var acc = 0.0;
            for (var j = 0; j < win; j++)
                acc += (double)samples[off + j] * samples[off + j];
            rms[i] = Math.Sqrt(acc / win);
            sum += rms[i];
        }
        var meanRms = sum / windowCount;

        // 2) 阈值 = max(均值×系数, 绝对下限)
        var floor = Math.Pow(10, options.AbsoluteFloorDb / 20.0);
        var threshold = Math.Max(meanRms * options.EnergyThresholdRatio, floor);

        // 3) 标记语音窗口，合并为段
        var speechWindows = new bool[windowCount];
        for (var i = 0; i < windowCount; i++)
            speechWindows[i] = rms[i] >= threshold;

        var gapWindows = (int)Math.Max(0, Math.Round(options.MergeGapSeconds / options.WindowSeconds));
        var minWindows = (int)Math.Max(1, Math.Round(options.MinSpeechSeconds / options.WindowSeconds));

        var start = -1;
        var lastSpeech = -1;
        double rmsAcc = 0;
        var rmsCount = 0;

        void Flush()
        {
            if (start < 0)
                return;
            var endWindow = lastSpeech + 1;
            var durationMs = endWindow * options.WindowSeconds * 1000.0;
            if (endWindow - start >= minWindows)
            {
                var avgDb = rmsCount > 0 ? 20 * Math.Log10(rmsAcc / rmsCount + 1e-12) : -120;
                result.Add(new VoiceSegment
                {
                    StartMs = start * options.WindowSeconds * 1000.0,
                    EndMs = durationMs,
                    RmsDb = Math.Round(avgDb, 1)
                });
            }
            start = -1;
            rmsAcc = 0;
            rmsCount = 0;
        }

        for (var i = 0; i < windowCount; i++)
        {
            if (speechWindows[i])
            {
                if (start < 0)
                    start = i;
                lastSpeech = i;
                rmsAcc += rms[i];
                rmsCount++;
            }
            else if (start >= 0 && i - lastSpeech > gapWindows)
            {
                Flush();
            }
        }
        Flush();

        return result;
    }
}
