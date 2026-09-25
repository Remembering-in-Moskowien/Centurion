using Centurion.Abstractions.Pipeline;
using Centurion.Core.Utils;
using Centurion.Abstractions;
using Centurion.Core.Managers;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 说话人画像算子（dub Phase 2）：为每个说话人挑选参考音频并写入 State.Extensions["DubSpeakerReferences"]。
/// 选段策略升级为 SNR 智能选段：在时长合适（2~8s，目标 4s）的候选句里，用 ffmpeg astats 估算语音 RMS，
/// 以媒体静音底噪为噪声底计算 SNR，选信噪比最高的一段；ffmpeg 分析失败时回退到最长句（Phase 1 行为）。
/// 手动指定 <c>--speaker-reference</c> 目录时直接使用目录中的 SPEAKER_xx.wav。
/// </summary>
public sealed class SpeakerProfilingOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<SpeakerProfilingOperator> logger)
    : PipelineOperatorBase<SpeakerProfilingOperator>(logger)
{
    /// <summary>参考音频最少时长（秒）：过短则跳过该句。</summary>
    internal const double MinReferenceSeconds = 2.0;

    /// <summary>参考音频目标时长（秒）：选段优先接近该值。</summary>
    internal const double TargetReferenceSeconds = 4.0;

    /// <summary>参考音频最大时长（秒）：裁剪时截断。</summary>
    private const double MaxReferenceSeconds = 8.0;

    /// <summary>每说话人参与 SNR 评分的候选句数量上限。</summary>
    private const int MaxCandidatesPerSpeaker = 3;

    /// <summary>算子名称。</summary>
    public override string Name => "Speaker Profiling";

    /// <summary>
    /// 为每个说话人挑选参考音频并写入 State.Extensions["DubSpeakerReferences"]（Dictionary&lt;string, string&gt;）。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1) 手动指定参考目录优先
        var manualDir = context.Config.SpeakerReferenceDir;
        if (!string.IsNullOrWhiteSpace(manualDir) && Directory.Exists(manualDir))
        {
            foreach (var file in Directory.GetFiles(manualDir, "*.wav", SearchOption.TopDirectoryOnly))
            {
                var speaker = Path.GetFileNameWithoutExtension(file);
                references[speaker] = file;
            }
            context.State.Extensions["DubSpeakerReferences"] = references;
            LogInfo($"Loaded {references.Count} manual speaker reference(s) from '{manualDir}'.");
            return;
        }

        // 2) 从媒体裁剪：SNR 智能选段
        var mediaPath = context.Config.InputFilePath;
        if (!string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath) && sentences.Count > 0)
        {
            var tempDir = context.State.PipelineTempDirectory ?? throw new InvalidOperationException("Pipeline temp directory is not initialized.");
            var speakers = sentences
                .Where(s => !string.IsNullOrWhiteSpace(s.Speaker))
                .Select(s => s.Speaker!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (speakers.Count > 0)
            {
                var ffmpeg = LocateFfmpeg();
                if (ffmpeg is not null)
                {
                    var noiseFloorDb = await EstimateNoiseFloorDbAsync(ffmpeg, mediaPath, cancellationToken);
                    foreach (var speaker in speakers)
                    {
                        var best = await PickBestReferenceAsync(ffmpeg, mediaPath, sentences, speaker, noiseFloorDb, cancellationToken);
                        if (best is null)
                            continue;

                        var refPath = Path.Combine(tempDir, $"ref_{Sanitize(speaker)}.wav");
                        await CropAsync(ffmpeg, mediaPath, best.Start / 1000.0,
                            Math.Min(MaxReferenceSeconds, (best.End - best.Start) / 1000.0), refPath, cancellationToken);
                        if (File.Exists(refPath) && new FileInfo(refPath).Length > 0)
                            references[speaker] = refPath;
                    }
                    LogInfo($"Noise floor {noiseFloorDb:0.0} dB; SNR-based selection applied.");
                }
                else
                {
                    context.State.Warnings.Add("ffmpeg not found; speaker reference extraction skipped (TTS will use default voice).");
                }
            }
        }

        context.State.Extensions["DubSpeakerReferences"] = references;
        LogInfo($"Prepared {references.Count} speaker reference(s).");
    }

    /// <summary>
    /// 对某说话人的候选句评分：时长越接近 <see cref="TargetReferenceSeconds"/> 越好，且 SNR（语音 RMS - 噪声底）越高越好。
    /// 无 SNR 数据时按时长接近度评分。
    /// </summary>
    /// <param name="candidate">候选句。</param>
    /// <param name="noiseFloorDb">噪声底 RMS（dB），null 表示不可用。</param>
    /// <param name="speechRmsDb">该句语音 RMS（dB），null 表示分析失败。</param>
    /// <returns>评分（越高越优）。</returns>
    internal static double ScoreCandidate(Sentence candidate, double? noiseFloorDb, double? speechRmsDb)
    {
        var duration = Math.Max(0, candidate.End - candidate.Start) / 1000.0;
        var durationScore = 1.0 / (1.0 + Math.Abs(duration - TargetReferenceSeconds) / TargetReferenceSeconds);

        double snrScore;
        if (speechRmsDb is not null && noiseFloorDb is not null)
            snrScore = Math.Clamp((speechRmsDb.Value - noiseFloorDb.Value) / 20.0, 0.0, 1.0);
        else if (speechRmsDb is not null)
            snrScore = Math.Clamp((speechRmsDb.Value + 60.0) / 60.0, 0.0, 1.0);
        else
            snrScore = 0.5;

        // SNR 权重更高：清晰度优先于时长完美度
        return 0.65 * snrScore + 0.35 * durationScore;
    }

    /// <summary>估算媒体整体噪声底（dB）：取媒体开头 0.6s 的 RMS；失败返回 null。</summary>
    private async Task<double?> EstimateNoiseFloorDbAsync(string ffmpeg, string mediaPath, CancellationToken ct)
    {
        try
        {
            var output = await processManager.ExecuteAsync(ffmpeg, new List<string>
            {
                "-hide_banner", "-ss", "0", "-t", "0.6", "-i", mediaPath,
                "-af", "astats=metadata=0,ametadata=print:key=lavfi.astats.Overall.RMS_level",
                "-f", "null", "-"
            }, ct);
            return ParseRmsDb(output);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Noise floor estimation failed: {Reason}", ex.Message);
            return null;
        }
    }

    /// <summary>分析单段音频的 RMS（dB）。</summary>
    private async Task<double?> AnalyzeRmsDbAsync(string ffmpeg, string mediaPath, double startSeconds, double durationSeconds, CancellationToken ct)
    {
        try
        {
            var output = await processManager.ExecuteAsync(ffmpeg, new List<string>
            {
                "-hide_banner", "-ss", startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                "-t", durationSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                "-i", mediaPath,
                "-af", "astats=metadata=0,ametadata=print:key=lavfi.astats.Overall.RMS_level",
                "-f", "null", "-"
            }, ct);
            return ParseRmsDb(output);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("RMS analysis failed for {Start}s: {Reason}", startSeconds, ex.Message);
            return null;
        }
    }

    /// <summary>从 ffmpeg astats/metadata 输出解析 RMS dB 值（形如 "lavfi.astats.Overall.RMS_level=-23.5dB"）。</summary>
    internal static double? ParseRmsDb(string ffmpegOutput)
    {
        foreach (var line in ffmpegOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf("RMS_level=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var value = line[(idx + "RMS_level=".Length)..].Trim().TrimEnd('d', 'B', 'b');
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var db))
                return db;
        }
        return null;
    }

    /// <summary>
    /// 为该说话人挑选最佳参考句：候选 = 时长 2~8s 且最接近目标时长的最多 <see cref="MaxCandidatesPerSpeaker"/> 句；
    /// 逐句分析 RMS 后按 <see cref="ScoreCandidate"/> 评分。无可用候选或全部分析失败时回退最长句（Phase 1 行为）。
    /// </summary>
    private async Task<Sentence?> PickBestReferenceAsync(
        string ffmpeg, string mediaPath, List<Sentence> sentences, string speaker, double? noiseFloorDb, CancellationToken ct)
    {
        var candidates = sentences
            .Where(s => s.Speaker != null && s.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase))
            .Where(s => Math.Max(0, s.End - s.Start) / 1000.0 >= MinReferenceSeconds)
            .OrderBy(s => Math.Abs((Math.Max(0, s.End - s.Start) / 1000.0) - TargetReferenceSeconds))
            .Take(MaxCandidatesPerSpeaker)
            .ToList();

        if (candidates.Count == 0)
        {
            // 无合适候选：回退最长句（Phase 1 语义）
            return sentences
                .Where(s => s.Speaker != null && s.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => Math.Max(0, s.End - s.Start))
                .FirstOrDefault();
        }

        var best = candidates[0];
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            var duration = Math.Max(0, candidate.End - candidate.Start) / 1000.0;
            var rms = await AnalyzeRmsDbAsync(ffmpeg, mediaPath, candidate.Start / 1000.0, Math.Min(MaxReferenceSeconds, duration), ct);
            var score = ScoreCandidate(candidate, noiseFloorDb, rms);
            Logger.LogDebug("Speaker {Speaker} candidate at {Start:F2}s: RMS {Rms:0.0} dB, score {Score:F3}", speaker, candidate.Start / 1000.0, rms, score);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // 若评分未明显优于第一个候选（都不可用时），保留最长句语义：best 已是首候选，无需额外处理
        return best;
    }

    private string? LocateFfmpeg()
    {
        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        }
        catch (Centurion.Abstractions.Exceptions.BinaryNotFoundException)
        {
            return null;
        }
    }

    private async Task CropAsync(string ffmpeg, string mediaPath, double startSeconds, double durationSeconds, string outputPath, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-y", "-ss", startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            "-i", mediaPath,
            "-t", durationSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            "-ac", "1", "-ar", "24000", "-c:a", "pcm_s16le", outputPath
        };
        await processManager.ExecuteAsync(ffmpeg, args, ct);
    }

    private static string Sanitize(string speaker) =>
        string.Concat(speaker.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
