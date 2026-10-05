using Centurion.Abstractions.Pipeline;
using Centurion.Core.Utils.Media;
using Centurion.Core.Utils.Parsing;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Source-parsing operator for the combine pipeline: parses all combine-command inputs
/// (media containers / bare subtitle files) uniformly into an in-process source list
/// (<see cref="SubtitleSourceItem"/>, written into State.Extensions), where each source
/// carries its own sentence set and ASS style table.
/// Media containers have their embedded subtitle tracks enumerated/extracted via
/// <see cref="MediaSubtitleExtractor"/> (filtered by --tracks); bare subtitle files
/// are parsed directly. A single-track extraction failure only warns and is skipped;
/// with zero sources an exception is thrown to abort.
/// </summary>
public sealed class CombineParseOperator(
    MediaSubtitleExtractor extractor,
    ILogger<CombineParseOperator> logger)
    : PipelineOperatorBase<CombineParseOperator>(logger)
{
    /// <summary>Key in State.Extensions for the source list (in-process; consumed by the merge operator).</summary>
    public const string SourcesKey = "CombineSources";

    /// <summary>Key in State.Extensions for the list of media input paths (written during command assembly).</summary>
    public const string MediaInputsKey = "CombineMediaInputs";

    /// <summary>Key in State.Extensions for the list of bare subtitle files (written during command assembly).</summary>
    public const string SubtitleInputsKey = "CombineSubtitleInputs";

    /// <summary>Key in State.Extensions for the track-number array (null = all subtitle tracks).</summary>
    public const string TrackFilterKey = "CombineTrackFilter";

    /// <summary>Operator name.</summary>
    public override string Name => "Combine Parse";

    /// <summary>
    /// Parses all inputs into a source list and writes it into the workflow state extension slot.
    /// </summary>
    /// <param name="context">The workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No subtitle source could be parsed from any input.</exception>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var mediaInputs = GetExtensionsValue<string[]>(context, MediaInputsKey) ?? [];
        var subtitleInputs = GetExtensionsValue<string[]>(context, SubtitleInputsKey) ?? [];
        var trackFilter = GetExtensionsValue<int[]?>(context, TrackFilterKey);
        var outputDir = context.State.PipelineTempDirectory
            ?? throw new InvalidOperationException("combine pipeline requires a temp directory for extracted tracks.");

        var sources = new List<SubtitleSourceItem>();

        foreach (var media in mediaInputs)
        {
            var tracks = await extractor.ListSubtitleTracksAsync(media, cancellationToken);
            if (tracks.Count == 0)
            {
                var warning = ConsoleServices.T("No extractable subtitle tracks in {0}.", media);
                ConsoleServices.Output.WriteWarning(warning);
                LogWarning($"No subtitle tracks in '{media}'.");
                continue;
            }

            if (trackFilter is { Length: > 0 })
                tracks = tracks.Where(t => trackFilter.Contains(t.TrackId)).ToList();

            if (tracks.Count == 0)
            {
                var warning = ConsoleServices.T("No subtitle tracks matched --tracks in {0}.", media);
                ConsoleServices.Output.WriteWarning(warning);
                LogWarning($"--tracks filtered all tracks out of '{media}'.");
                continue;
            }

            foreach (var track in tracks)
            {
                var extracted = await extractor.ExtractTrackAsync(media, track, outputDir, cancellationToken);
                if (extracted is null)
                {
                    var warning = ConsoleServices.T("Subtitle track {0} of {1} could not be extracted; skipped.", track.TrackId, media);
                    ConsoleServices.Output.WriteWarning(warning);
                    LogWarning(warning);
                    continue;
                }

                var name = BuildMediaSourceName(media, track);
                var parsed = ParseQuietly(extracted, name, context.Config.Language);
                if (parsed is not null)
                    sources.Add(parsed);
            }
        }

        foreach (var file in subtitleInputs)
        {
            var name = Path.GetFileName(file);
            var parsed = ParseQuietly(file, name, context.Config.Language);
            if (parsed is not null)
                sources.Add(new SubtitleSourceItem
                {
                    Name = name,
                    Sentences = parsed.Sentences,
                    Styles = parsed.Styles
                });
        }

        if (sources.Count == 0)
            throw new InvalidOperationException("No subtitle sources parsed from the given inputs.");

        context.State.Extensions[SourcesKey] = sources;
        var summary = string.Join("; ", sources.Select(s => $"{s.Name}: {s.Sentences.Count} lines"));
        LogInfo($"Parsed {sources.Count} subtitle source(s): {summary}");
    }

    /// <summary>Parses a single subtitle file into a source; on parse failure only warns and returns null (a corrupt track does not block the combine).</summary>
    private SubtitleSourceItem? ParseQuietly(string path, string displayName, string? language)
    {
        try
        {
            var parsed = SubtitleFileParser.Parse(path, language);
            return new SubtitleSourceItem
            {
                Name = displayName,
                Sentences = parsed.Sentences,
                Styles = parsed.Styles
            };
        }
        catch (Exception ex)
        {
            var warning = ConsoleServices.T("Subtitle source {0} could not be parsed; skipped ({1}).", displayName, ex.Message);
            ConsoleServices.Output.WriteWarning(warning);
            LogWarning(warning);
            return null;
        }
    }

    /// <summary>Source display name: media file name + track number + (language/name).</summary>
    private static string BuildMediaSourceName(string mediaPath, MkvTrackInfo track)
    {
        var qualifier = !string.IsNullOrWhiteSpace(track.Name) ? track.Name
            : !string.IsNullOrWhiteSpace(track.Language) && !track.Language.Equals("und", StringComparison.OrdinalIgnoreCase)
                ? track.Language
                : null;
        var suffix = qualifier is null ? "" : $" ({qualifier})";
        return $"{Path.GetFileName(mediaPath)} #track {track.TrackId}{suffix}";
    }

    /// <summary>Reads a strongly typed value from Extensions (returns null and warns on a type mismatch).</summary>
    private static T? GetExtensionsValue<T>(SubtitleWorkflowContext context, string key)
    {
        if (!context.State.Extensions.TryGetValue(key, out var raw))
            return default;
        if (raw is T typed)
            return typed;
        return default;
    }
}
