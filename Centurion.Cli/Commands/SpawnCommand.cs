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
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; } = null!;

    [CommandOption("-o|--outputfile <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public required FileInfo OutputFile { get; init; } = null!;

    [CommandOption("-m|--model <MODEL>")]
    [Description("Whisper model: tiny/base/small/medium/large, default is base")]
    public string ModelName { get; init; } = "base";

    [CommandOption("-p|--prompt <PROMPT>")]
    [Description("Whisper initial prompt")]
    public string? InitialPrompt { get; init; }

    [CommandOption("-l|--lang|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    [CommandOption("--max-length <MAX_LENGTH>")]
    [Description("Maximum characters per subtitle line, default 80")]
    public int MaxLength { get; init; } = 80;

    [CommandOption("--target-length <TARGET_LENGTH>")]
    [Description("Target characters per line, default 50")]
    public int TargetLength { get; init; } = 50;

    [CommandOption("--spread-range <SPREAD_RANGE>")]
    [Description("Spread range for line length distribution, default 10")]
    public int SpreadRange { get; init; } = 10;

    [CommandOption("--num-speakers <NUM>")]
    [Description("Number of speakers (0 = auto detect), default 0")]
    public int NumSpeakers { get; init; } = 0;

    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS subtitles with karaoke effects (\\K tags)")]
    public bool Karaoke { get; init; }

    [CommandOption("--align")]
    [Description("(Currently disabled) Forced alignment is unavailable and will be ignored")]
    public bool Align { get; init; }
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

            var ext = Path.GetExtension(inputPath).ToLowerInvariant();
            if (!MediaFileExtensions.Contains(ext))
                throw new ArgumentException($"Unsupported media file type: {ext}");

            if (settings.Align)
            {
                ConsoleServices.Output?.WriteLine("[yellow]Warning: --align option is currently disabled and will be ignored.[/]");
            }

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
                Align = false  // force disable alignment
            };

            var request = new OperatorsRequest<MediaGenerationRequest> { Payload = payload };
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