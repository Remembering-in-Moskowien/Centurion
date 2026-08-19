using System.ComponentModel;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Operators;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class SpawnSettings : CommandSettings
{
    [CommandOption("-i|--inputfile <INPUT_FILE>", true)]
    [Description("输入的媒体文件")]
    public required FileInfo InputFile { get; init; } = null!;

    [CommandOption("-o|--outputfile <OUTPUT_FILE>")]
    [Description("输出的ASS字幕文件")]
    public required FileInfo OutputFile { get; init; } = null!;

    [CommandOption("-m|--model <MODEL>")]
    [Description("Whisper模型：tiny/base/small/medium/large，默认base")]
    public string ModelName { get; init; } = "base";

    [CommandOption("-p|--prompt <PROMPT>")]
    [Description("Whisper 初始提示词")]
    public string? InitialPrompt { get; init; }

    [CommandOption("-l|--lang|--language <LANG>")]
    [Description("音频识别语言代码，默认 en")]
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

public sealed class SpawnCommand(SubtitleGeneratorOperator generator)
    : AsyncCommand<SpawnSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        SpawnSettings settings,
        CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");

            // 验证输入是否为媒体文件
            var ext = Path.GetExtension(inputPath).ToLowerInvariant();
            if (!MediaFileExtensions.Contains(ext))
                throw new ArgumentException($"Unsupported media file type: {ext}");

            var payload = new MediaGenerationRequest
            {
                InputFilePath = inputPath,
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

            var request = new OperatorsRequest<MediaGenerationRequest> { Payload = payload };
            // 使用强类型 ProcessAsync（无需泛型参数）
            var result = await generator.ProcessAsync(request, ct);

            await File.WriteAllTextAsync(outputPath, result.Document.ToString(), ct);

            AnsiConsole.MarkupLine($"[green]Generation succeeded:[/]{outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleServices.Output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];
}