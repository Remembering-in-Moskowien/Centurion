using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

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

    public override string Name => "Audio Conversion (FFmpeg)";

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

/// <summary>
/// Exception thrown when audio conversion via FFmpeg fails.
/// </summary>
public class AudioConversionException : Exception
{
    public AudioConversionException(string message) : base(message)
    {
    }

    public AudioConversionException(string message, Exception inner) : base(message, inner)
    {
    }
}