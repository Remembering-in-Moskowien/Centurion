using System.Text.Json;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Media;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Subtitle track check operator: runs at the very front of the generate/calibrate/time-align pipeline,
/// uses mkvtoolnix (mkvmerge -i) to detect whether the input media already contains subtitle tracks,
/// warns the user if so, and writes the check report to the output directory ({output}.tracks.json).
/// A failed check or a missing tool does not block the pipeline; it only logs a warning.
/// </summary>
public sealed class SubtitleTrackCheckerOperator(
    MkvToolNixChecker checker,
    ILogger<SubtitleTrackCheckerOperator> logger)
    : PipelineOperatorBase<SubtitleTrackCheckerOperator>(logger)
{
    /// <summary>Operator name.</summary>
    public override string Name => "Subtitle Track Check";

    /// <summary>
    /// Runs the check: read input media -> probe tracks with mkvmerge -> warn about existing subtitles -> write the report.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var input = context.Config.InputFilePath;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            LogInfo("No media input; subtitle track check skipped.");
            return;
        }

        var result = await checker.CheckAsync(input, cancellationToken);
        context.State.Extensions["SubtitleTrackCheck"] = result;

        if (!result.Checked)
        {
            LogWarning($"Subtitle track check skipped: {result.Message}");
            return;
        }

        // Existing subtitle track(s): warn the user up front
        if (result.HasSubtitleTracks)
        {
            var summary = string.Join("; ", result.SubtitleTracks.Select(t => t.Summary));
            ConsoleServices.Output.WriteWarning(
                ConsoleServices.T("Existing subtitle track(s) found in {0}: {1}", result.SourceFile, summary));
            LogWarning($"Existing subtitle tracks in '{result.SourceFile}': {summary}");
        }
        else
        {
            ConsoleServices.Output.WriteInfo(
                ConsoleServices.T("No existing subtitle tracks in {0}.", result.SourceFile));
            LogInfo($"No subtitle tracks in '{result.SourceFile}'.");
        }

        // Write the report to the output directory (same directory and base name as the output subtitle, as .tracks.json)
        var outputPath = context.Config.OutputFilePath;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var reportPath = Path.ChangeExtension(outputPath, ".tracks.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Subtitle track report: {0}", reportPath));
            LogInfo($"Subtitle track report written to '{reportPath}'.");
        }
    }
}
