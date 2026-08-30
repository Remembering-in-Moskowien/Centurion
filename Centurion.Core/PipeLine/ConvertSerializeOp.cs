// File: Centurion.Core/Operators/ConvertSerializeOp.cs

using Centurion.Core.Abstractions;
using Centurion.Core.Models;

namespace Centurion.Core.PipeLine;

public class ConvertSerializeOp : IPipelineOperator
{
    public string Name => "ConvertSerialize";

    public async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var subtitle = context.State.ParsedSubtitle;
        if (subtitle == null || subtitle.Count == 0)
            throw new InvalidOperationException("No parsed subtitle found in context. Ensure ConvertParseOp executed first.");

        var outputPath = context.Config.OutputFilePath;
        if (string.IsNullOrEmpty(outputPath))
            outputPath = Path.ChangeExtension(context.Config.InputFilePath, ".ass");

        var builder = new AssSubBuilder().WithDefaultValues();
        var lines = new List<AssSubLine>();
        
        foreach (var subtitleModel in subtitle)
        {
            lines.Add(new AssSubLineBuilder()
                .WithComment(false)
                .WithLayer(0)
                .WithStart(subtitleModel.StartTime)
                .WithEnd(subtitleModel.EndTime)
                .WithStyle("Default")
                .WithName(string.Empty)
                .WithMarginL(0)
                .WithMarginR(0)
                .WithMarginV(0)
                .WithEffect(string.Empty)
                .WithText(string.Join(@"\N", subtitleModel.Lines.ToArray()))
                .Build());
        }

        builder = builder.WithLines(lines);
        var assDoc = builder.Build();

        await File.WriteAllTextAsync(outputPath, assDoc.ToString(), cancellationToken);

        context.State.IsFinalized = true;
    }
}