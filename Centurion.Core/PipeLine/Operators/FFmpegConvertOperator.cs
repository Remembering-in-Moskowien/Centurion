using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using Centurion.Models.Workflow;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// Pipeline operator for audio conversion via FFmpeg.
/// Converts input audio to 16kHz mono WAV (PCM s16le) for downstream processing.
/// </summary>
public class FFmpegConvertOperator(
    ITempDirectoryManager tempManager,
    ILogger<FFmpegConvertOperator> logger) : PipelineOperatorBase<FFmpegConvertOperator>(logger)
{
    private readonly ITempDirectoryManager _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));
    private const int TargetSampleRate = 16000;
    private const int TargetChannels = 1;
    private const string TargetCodec = "pcm_s16le";

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Audio Conversion (FFmpeg)";

    /// <summary>
    /// 用 FFmpeg 将输入音频转换为 16kHz 单声道 WAV（PCM s16le），
    /// 输出写入工作流状态中的 ConvertedAudioPath。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供输入音频路径与临时目录。</param>
    /// <param name="cancellationToken">用于取消音频转换过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.IsAudioConverted)
        {
            LogInfo("Audio already converted, skipping.");
            return;
        }

        var inputPath = context.Config.InputFilePath;
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input audio file not found: {inputPath}");

        // 使用 Pipeline 共享的临时目录
        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrEmpty(tempDir))
            throw new InvalidOperationException("Pipeline temporary directory not set.");

        var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + "_16k.wav";
        var outputPath = Path.Combine(tempDir, outputFileName);

        OnProgress(10, $"Converting audio: {Path.GetFileName(inputPath)} to 16kHz mono WAV...");

        try
        {
            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, false, options => options
                    .WithAudioCodec(TargetCodec)
                    .WithAudioSamplingRate(TargetSampleRate)
                    .WithCustomArgument($"-ac {TargetChannels}")
                    .ForceFormat("wav"))
                .ProcessAsynchronously();

            if (!File.Exists(outputPath))
                throw new InvalidOperationException($"FFmpeg did not produce output file: {outputPath}");

            context.State.ConvertedAudioPath = outputPath;
            context.State.IsAudioConverted = true;

            OnProgress(100, "Audio conversion completed.");
            LogInfo($"Converted audio saved to: {outputPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError($"Audio conversion failed: {ex.Message}");
            throw new AudioConversionException($"FFmpeg conversion failed for '{inputPath}'", ex);
        }
    }
}
