using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Managers;
using Centurion.Core.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 人声分离算子（Demucs-rs，可选增强）。
/// 将音频分离出人声轨（--two-stems vocals），写入 State.VocalsPath，
/// 供转录与说话人分割优先消费。仅在 WorkflowConfig.VocalSeparation 开启时执行；
/// 失败为非致命错误，仅记录警告并回退原始音频继续。
/// </summary>
public sealed class VocalSeparationOperator(
    IToolManagerFactory toolFactory,
    ProcessManager processManager,
    ILogger<VocalSeparationOperator> logger) : PipelineOperatorBase<VocalSeparationOperator>(logger)
{
    private readonly IToolManagerFactory _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
    private readonly ProcessManager _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

    public override string Name => "Vocal Separation";

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. 开关（默认关闭：对纯语音素材无价值且耗时长）
        if (!config.VocalSeparation)
        {
            LogInfo("Vocal separation disabled (VocalSeparation = false).");
            return;
        }

        // 2. 检查点：已完成且产物存在则跳过
        if (context.State.IsVocalsSeparated
            && !string.IsNullOrEmpty(context.State.VocalsPath)
            && File.Exists(context.State.VocalsPath))
        {
            LogInfo("Vocal separation already exists, skipping.");
            return;
        }

        // 3. 输入音频（优先预处理后的音频）
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for vocal separation: {inputPath}");
            return;
        }

        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping vocal separation.");
            return;
        }

        var tool = _toolFactory.Create("demucsrs", config.Device);
        try
        {
            // 4. 确保工具已就绪（首次自动下载）
            OnProgress(5, "Ensuring demucs-rs tool...");
            await tool.EnsureToolAsync(cancellationToken);

            // 5. 执行分离
            var outputDir = Path.Combine(tempDir, $"vocalsep_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);
            var args = BuildArguments(config.VocalSeparationModel, inputPath, outputDir);

            LogInfo($"Running Demucs vocal separation (model {config.VocalSeparationModel})...");
            OnProgress(20, "Separating vocals (this may take a while)...");
            await _processManager.ExecuteAsync(tool.ExecutablePath, args, cancellationToken);

            // 6. 定位人声轨（递归查找，兼容不同输出目录布局）
            var vocalsFile = FindVocalsFile(outputDir);
            if (vocalsFile is null)
            {
                LogWarning("Demucs finished but no 'vocals' stem was found in output; continuing with original audio.");
                return;
            }

            var finalPath = Path.Combine(tempDir, $"vocals_{Guid.NewGuid():N}.wav");
            File.Move(vocalsFile, finalPath);

            context.State.VocalsPath = finalPath;
            context.State.IsVocalsSeparated = true;
            OnProgress(100, "Vocal separation completed.");
            LogInfo($"Vocals saved to: {finalPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 非致命：分离失败不中断字幕生成
            LogWarning($"Vocal separation failed; continuing with original audio. {ex.Message}");
        }
    }

    /// <summary>
    /// 构建 Demucs 命令行参数（internal，便于单元测试）。
    /// 输出布局：&lt;outputDir&gt;/&lt;model&gt;/&lt;input-basename&gt;/vocals.wav（由 FindVocalsFile 递归定位）。
    /// </summary>
    internal static string BuildArguments(string model, string inputPath, string outputDir) =>
        $"-n {model} --two-stems vocals -o \"{outputDir}\" \"{inputPath}\"";

    /// <summary>
    /// 在 Demucs 输出目录中递归查找人声轨文件（兼容不同版本/布局差异）。
    /// </summary>
    internal static string? FindVocalsFile(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return null;

        var candidates = Directory.GetFiles(outputDir, "vocals.wav", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(outputDir, "vocals.flac", SearchOption.AllDirectories))
            .ToList();
        return candidates.FirstOrDefault();
    }
}
