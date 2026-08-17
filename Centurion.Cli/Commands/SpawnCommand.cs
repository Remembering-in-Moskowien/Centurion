using System.ComponentModel;
using Centurion.Core;
using Centurion.Core.Models;
// localization removed; strings hard-coded
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

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

    [CommandOption("--num-speakers <NUM>")]
    [Description("说话人人数（0 表示自动检测），默认 0")]
    public int NumSpeakers { get; init; } = 0;

    [CommandOption("-k|--karaoke")]
    [Description("生成带卡拉OK特效的ASS字幕（\\K标签）")]
    public bool Karaoke { get; init; }

    [CommandOption("--mfa")]
    [Description("使用 MFA（Montreal Forced Aligner）进行高精度强制对齐")]
    public bool UseMfa { get; init; }
}

public sealed class SpawnCommand(AssSubSpawner subSpawner)
    : AsyncCommand<SpawnSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        SpawnSettings settings,
        CancellationToken ct)
    {
        try
        {
            // 路径处理
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");
            var spawnOptions = GetSpawnerOptions(inputPath);

            var genOptions = new SubtitleGenerationOptions
            {
                ModelName = settings.ModelName,
                Language = settings.Language,
                InitialPrompt = settings.InitialPrompt,
                MaxLength = settings.MaxLength,
                TargetLength = settings.TargetLength,
                SpreadRange = settings.SpreadRange,
                NumSpeakers = settings.NumSpeakers,
                Karaoke = settings.Karaoke,
                UseMfa = settings.UseMfa
            };

            var assDoc = await subSpawner.AssSpawnerAsync(spawnOptions, inputPath, genOptions, ct);
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            AnsiConsole.MarkupLine($"[green]Generation succeeded:[/]{outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Gentle") || ex.Message.Contains("connection"))
            {
                ConsoleServices.Output.WriteError("Gentle service request failed.");
                ConsoleServices.Output.WriteLine(ex.Message);
            }
            else
            {
                ConsoleServices.Output.WriteError($"Error: {ex.Message}");
            }

            return 1;
        }
    }

    private AssSubSpawnerOptions GetSpawnerOptions(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return ext switch
        {
            ".srt" => AssSubSpawnerOptions.Srt,
            _ when MediaFileExtensions.Contains(ext) => AssSubSpawnerOptions.Media,
            _ => throw new ArgumentException(string.Format("Unsupported input file type: {0}", ext), nameof(inputPath))
        };
    }

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];
}