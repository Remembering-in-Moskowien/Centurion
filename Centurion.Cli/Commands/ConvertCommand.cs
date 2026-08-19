using System.ComponentModel;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Operators;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class ConvertSettings : CommandSettings
{
    [CommandOption("-i|--inputfile <INPUT_FILE>", true)]
    [Description("输入的SRT字幕文件")]
    public required FileInfo InputFile { get; init; } = null!;

    [CommandOption("-o|--outputfile <OUTPUT_FILE>")]
    [Description("输出的ASS字幕文件")]
    public required FileInfo OutputFile { get; init; } = null!;

    [CommandOption("-f|--format <FORMAT>")]
    [Description("输入字幕格式（默认根据扩展名自动识别，可指定 srt/vtt/lrc 等）")]
    public string? Format { get; init; }
}

public sealed class ConvertCommand(SubtitleConverterOperator converter)
    : AsyncCommand<ConvertSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ConvertSettings settings,
        CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");

            var payload = new SubtitleConvertRequest
            {
                FilePath = inputPath,
                Format = settings.Format
            };
            var request = new OperatorsRequest<SubtitleConvertRequest> { Payload = payload };

            // 使用强类型 ProcessAsync（无需泛型参数）
            var result = await converter.ProcessAsync(request, ct);

            await File.WriteAllTextAsync(outputPath, result.Document.ToString(), ct);

            AnsiConsole.MarkupLine($"[green]Conversion succeeded:[/]{outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleServices.Output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}