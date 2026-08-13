using Centurion.Core;
using Centurion.Cli.Console;
using System.ComponentModel;
using Centurion.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

ConsoleServices.Output = new SpectreConsoleOutput();
ConsoleServices.Progress = new SpectreProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

// 3. 程序入口（顶级语句）
const string version = "alpha";
AnsiConsole.Write(new FigletText($"Centurion {version}"));

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Cancellation requested...");
};

// 构建CLI应用
var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
});

// 执行并返回退出码
return await app.RunAsync(args);

// 1. 定义 spawn 命令参数模型
public sealed class SpawnSettings : CommandSettings
{
    [CommandOption("-i|--inputfile <INPUT_FILE>", true)]
    [Description("输入的SRT/媒体文件")]
    public required FileInfo InputFile { get; init; } = null!;

    [CommandOption("-o|--outputfile <OUTPUT_FILE>")]
    [Description("输出的ASS字幕文件")]
    public required FileInfo OutputFile { get; init; } = null!;

    [CommandOption("-m|--model <MODEL>")]
    [Description("Whisper模型：tiny/base/small/medium/large，默认base")]
    public string ModelName { get; init; } = "base";
    
    [CommandOption("-p|--prompt <PROMPT>")]
    [Description("Whisper 初始提示词，用于引导转录（例如：'Minecraft Bedwars'）")]
    public string? InitialPrompt { get; init; }

    [CommandOption("-l|--lang|--language <LANG>")]
    [Description("音频识别语言代码，默认 en（中文使用 zh）")]
    public string Language { get; init; } = "en";
    
    [CommandOption("--max-length <MAX_LENGTH>")]
    [Description("分句最大长度（字符数），默认80")]
    public int MaxLength { get; init; } = 80;

    [CommandOption("--target-length <TARGET_LENGTH>")]
    [Description("目标分句长度（字符数），默认50")]
    public int TargetLength { get; init; } = 50;

    [CommandOption("--spread-range <SPREAD_RANGE>")]
    [Description("长度分布的扩散范围，默认10")]
    public int SpreadRange { get; init; } = 10;
}

// 2. 实现Spawn命令执行逻辑
public sealed class SpawnCommand : AsyncCommand<SpawnSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SpawnSettings settings,
        CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");
            var spawnOptions = GetSpawnerOptions(inputPath);

            var subtitleGenerationOptions = new SubtitleGenerationOptions
            {
                MaxLength = settings.MaxLength,
                TargetLength = settings.TargetLength,
                InitialPrompt = settings.InitialPrompt,
                Language = settings.Language,
                ModelName = settings.ModelName,
                SpreadRange = settings.SpreadRange
            };

            // 异步获取字幕文档
            var assDoc =
                await AssSubSpawner.AssSpawnerAsync(spawnOptions, inputPath, subtitleGenerationOptions, ct);
            // 写入ASS文件
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);
            AnsiConsole.MarkupLine($"[green]{Localization.Get("SuccessGenerated")}[/] {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Localization.Get("ErrorLabel")}[/] {ex.Message}");
            return 1;
        }
    }

    /// 媒体文件后缀常量（音频+主流/小众视频格式）
    private static readonly HashSet<string> MediaFileExtensions =
    [
        // 音频
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        // 主流视频
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        // 小众视频封装
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];

    /// 后缀映射生成Spawner配置
    private static AssSubSpawnerOptions GetSpawnerOptions(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        return ext switch
        {
            ".srt" => AssSubSpawnerOptions.Srt,
            _ when MediaFileExtensions.Contains(ext) => AssSubSpawnerOptions.Media,
            _ => throw new ArgumentException(Localization.Get("UnsupportedInputFileType", ext), nameof(inputPath))
        };
    }
}