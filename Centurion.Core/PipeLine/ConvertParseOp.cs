// File: Centurion.Core/Operators/ConvertParseOp.cs

using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using SubtitlesParserV2;

namespace Centurion.Core.PipeLine;

public class ConvertParseOp : IPipelineOperator
{
    public string Name => "ConvertParse";

    public async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var inputPath = context.Config.InputFilePath;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Subtitle file not found.", inputPath);

        using var stream = File.OpenRead(inputPath);
        var subtitle = SubtitleParser.ParseStream(stream)?.Subtitles;

        if (subtitle == null || subtitle.Count == 0)
            throw new InvalidOperationException("No subtitle items parsed.");

        // 直接赋值给强类型属性
        context.State.ParsedSubtitle = subtitle;
    }
}