using Centurion.Cli.Commands.Settings;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class SpawnCommand : AsyncCommand<SpawnSettings>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITempDirectoryManager _tempManager;

    public SpawnCommand(IServiceProvider serviceProvider, ITempDirectoryManager tempManager)
    {
        _serviceProvider = serviceProvider;
        _tempManager = tempManager;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, SpawnSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");

            // 验证媒体文件扩展名
            if (!MediaFileExtensions.Contains(Path.GetExtension(inputPath).ToLowerInvariant()))
                throw new ArgumentException($"Unsupported media file type: {Path.GetExtension(inputPath)}");

            // 1. 构建工作流配置（从命令行参数映射）
            var config = new WorkflowConfig
            {
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                Language = settings.Language,
                NumSpeakers = settings.NumSpeakers,
                KaraokeMode = settings.Karaoke,
                CacheDirectory = "./cache",

                // 转录模块
                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                InitialPrompt = settings.InitialPrompt,

                // 分句模块
                SplitStrategy = settings.Splitter,
                MaxSentenceLength = settings.MaxLength,
                TargetSentenceLength = settings.TargetLength,
                SpreadRange = settings.SpreadRange,
                MergeGapSeconds = 1.5, // 可暴露为参数，暂固定
                EnablePunctuationRewrite = true,
                SplitterModel = settings.SplitterModel,
                SplitterApiKey = settings.SplitterApiKey,

                // 对齐模块
                AlignerEngine = settings.Aligner,
                AlignerModel = settings.AlignerModel
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // 2. 创建管道临时目录
            await using var tempDir = await _tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // 3. 从 DI 获取算子实例（每个算子内部使用策略工厂）
            var ffmpegOp = _serviceProvider.GetRequiredService<FFmpegConvertOperator>();
            var transcribeOp = _serviceProvider.GetRequiredService<TranscribeOperator>();
            var splitOp = _serviceProvider.GetRequiredService<SentenceSplitOperator>();
            var alignOp = _serviceProvider.GetRequiredService<AlignmentOperator>();

            // 4. 构建管道（固定顺序）
            var pipeline = new List<IPipelineOperator> { ffmpegOp, transcribeOp, splitOp };
            if (!string.IsNullOrEmpty(config.AlignerEngine))
                pipeline.Add(alignOp);

            // 5. 依次执行
            foreach (var op in pipeline)
            {
                if (op is IHealthCheckableOperator healthy)
                    await healthy.CheckHealthAsync(ct);

                await op.ExecuteAsync(workflowContext, ct);
            }

            // 6. 生成 ASS 字幕
            var assBuilder = AssSubBuilder.FromWorkflow(workflowContext);
            var assDoc = assBuilder.Build();

            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            AnsiConsole.MarkupLine($"[green]Subtitle generation completed: {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static readonly HashSet<string> MediaFileExtensions = new()
    {
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    };
}