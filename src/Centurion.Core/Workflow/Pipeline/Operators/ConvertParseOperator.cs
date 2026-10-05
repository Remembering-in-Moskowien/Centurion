using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Convert pipeline: uses <see cref="SubtitleFileParser"/> to parse the input subtitle file
/// and converts every subtitle entry into a Sentence object stored in TranscribeSentences.
/// ASS input uses the project's own parser, preserving the style table and per-line styles.
/// </summary>
public sealed class ConvertParseOperator : IPipelineOperator
{
    /// <summary>Display name of the operator in the pipeline.</summary>
    public string Name => "ConvertParse";

    /// <summary>
    /// Parses the input subtitle file and converts each subtitle entry into a
    /// <see cref="Centurion.Models.Sentence"/>, writing it into the workflow state.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing the subtitle file path.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the parsing process.</param>
    public Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var inputPath = context.Config.SubtitleFilePath ?? context.Config.InputFilePath;
        var parsed = SubtitleFileParser.Parse(inputPath!, context.Config.Language);

        var sentences = parsed.Sentences;

        // Write the ASS style table into state (so build rendering can reuse the original styles)
        if (parsed.Styles.Count > 0)
            context.State.Styles = [.. parsed.Styles];

        // Keep an independent baseline; the convert pipeline still uses TranscribeSentences as the existing output slot.
        context.State.SubtitleSentences = sentences.Select(CloneSentence).ToList();
        context.State.TranscribeSentences = sentences;
        context.State.CurrentSentences = sentences;
        return Task.CompletedTask;
    }

    private static Centurion.Models.Sentence CloneSentence(Centurion.Models.Sentence source)
    {
        return new Centurion.Models.Sentence
        {
            Text = source.Text,
            CleanedText = source.CleanedText,
            Start = source.Start,
            End = source.End,
            SkipRender = source.SkipRender,
            Style = source.Style,
            Words = source.Words.Select(word => new Centurion.Models.Word
            {
                Text = word.Text,
                Start = word.Start,
                End = word.End,
                Speaker = word.Speaker,
                PosTag = word.PosTag,
                Status = word.Status
            }).ToList()
        };
    }
}
