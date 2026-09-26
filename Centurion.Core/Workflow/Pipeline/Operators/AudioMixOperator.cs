using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 混音算子（dub Phase 2/3）：把各对齐后的 TTS 片段按字幕时间轴放置到完整时长音轨上。
/// 支持三种模式：
/// 1) 纯人声：TTS 片段 amix 到静音底轨 → loudnorm 响度归一化；
/// 2) 伴奏 + ducking：提供 <c>DubBackgroundPath</c> 时，伴奏先经 sidechaincompress 被人声侧链压低，
///    再与人声混合（默认开启，可用 <c>DubDucking=false</c> 关闭）；
/// 3) 相邻段重叠：按 TimeAlignment 阶段计算的 MixOffsetMs 压叠（后段前移）。
/// 输出最终译制 wav。
/// </summary>
public sealed class AudioMixOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<AudioMixOperator> logger)
    : PipelineOperatorBase<AudioMixOperator>(logger)
{
    private const int SampleRate = 44100;

    /// <summary>算子名称。</summary>
    public override string Name => "Audio Mix";

    /// <summary>
    /// 生成完整时长底音轨并把各段 adelay 对齐后混合输出；有伴奏时启用 ducking，最终 loudnorm 归一化。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.DubSegments is not { Count: > 0 } segments)
            return;

        var ffmpeg = LocateFfmpeg();
        if (ffmpeg is null)
        {
            context.State.Warnings.Add("ffmpeg not found; audio mixing skipped.");
            LogWarning("ffmpeg not found; audio mixing skipped.");
            return;
        }

        var ready = segments.Where(s => !s.Skipped && File.Exists(s.SynthesizedWavPath)).ToList();
        if (ready.Count == 0)
        {
            context.State.Warnings.Add("No synthesized segments; audio mix skipped.");
            LogWarning("No synthesized segments; audio mix skipped.");
            return;
        }

        var totalMs = Math.Max(1000, (int)Math.Ceiling(segments.Max(s => s.TargetEndMs)));
        var wavPath = context.State.DubOutputWavPath
            ?? Path.ChangeExtension(context.Config.SubtitleFilePath ?? context.Config.InputFilePath ?? context.Config.OutputFilePath, ".dub.wav");
        var outputPath = !string.IsNullOrWhiteSpace(wavPath)
            ? wavPath!
            : Path.ChangeExtension(context.Config.InputFilePath, ".dub.wav")!;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var background = context.Config.DubBackgroundPath;
        var useDucking = context.Config.DubDucking && !string.IsNullOrWhiteSpace(background) && File.Exists(background);
        if (useDucking)
            LogInfo($"Mixing with background '{background}' + ducking.");
        else if (!string.IsNullOrWhiteSpace(background) && !File.Exists(background))
            context.State.Warnings.Add($"Background file not found: {background}; mixing vocals only.");

        await MixAsync(ffmpeg, ready, totalMs, outputPath, context.Config.DubLoudnessTarget, background, useDucking, cancellationToken);
        LogInfo($"Dubbed audio written to '{outputPath}' ({ready.Count} segments, {totalMs}ms).");
    }

    /// <summary>
    /// 用 ffmpeg filter_complex 完成混音：
    /// - 各 TTS 段以 (TargetStartMs + MixOffsetMs) 偏移叠加为 [vox]；
    /// - 有伴奏且 ducking 开启时 [bg][vox]sidechaincompress 生成 [duckbg]，再 amix；
    /// - 最后 loudnorm 归一化到目标 LUFS。
    /// </summary>
    private async Task MixAsync(string ffmpeg, List<DubSegment> segments, int totalMs, string outputPath,
        double loudnessTarget, string? backgroundPath, bool ducking, CancellationToken ct)
    {
        var inputs = new List<string> { "-y", "-f", "lavfi", "-i", $"anullsrc=r={SampleRate}:cl=stereo", "-t", (totalMs / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var segment in segments)
        {
            inputs.Add("-i");
            inputs.Add(segment.SynthesizedWavPath!);
        }
        if (backgroundPath is not null)
        {
            inputs.Add("-i");
            inputs.Add(backgroundPath);
        }

        var filters = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var delay = Math.Max(0, (int)Math.Round(segments[i].TargetStartMs + segments[i].MixOffsetMs));
            filters.Add($"[{i + 1}:a]aresample={SampleRate},adelay={delay}|{delay}[a{i}]");
        }

        var vocalMixInputs = string.Join("", Enumerable.Range(0, segments.Count).Select(i => $"[a{i}]"));
        filters.Add($"{vocalMixInputs}[0:a]amix=inputs={segments.Count + 1}:normalize=0:duration=first[vox]");

        var loudnorm = $"loudnorm=I={loudnessTarget.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}:TP=-1.5:LRA=11";
        if (ducking && backgroundPath is not null)
        {
            // 伴奏轨后置输入：索引 = 1 + segments.Count。
            // [vox] 被两个 filter 引用（sidechain 侧链 + 最终混合），ffmpeg 7 直接 fan-out 会解析失败，
            // 故先用 asplit 显式分流。
            var bgIndex = 1 + segments.Count;
            filters.Add($"[vox]asplit=2[v1][v2]");
            filters.Add($"[{bgIndex}:a]aresample={SampleRate}[bg]");
            filters.Add($"[bg][v1]sidechaincompress=threshold=0.05:ratio=8:attack=50:release=400[duckbg]");
            filters.Add($"[duckbg][v2]amix=inputs=2:normalize=0:duration=first[mix]");
            filters.Add($"[mix]{loudnorm}[out]");
        }
        else
        {
            filters.Add($"[vox]{loudnorm}[out]");
        }

        var args = inputs.Concat(["-filter_complex", string.Join(";", filters), "-map", "[out]", "-c:a", "pcm_s16le", outputPath]).ToList();
        await processManager.ExecuteAsync(ffmpeg, args, ct);
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
}
