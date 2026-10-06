using Centurion.Models.Ass;

namespace Centurion.Models.Workflow;

/// <summary>
/// Parsed result for one subtitle input source of the combine command: a source (a subtitle track inside the media,
/// or a bare subtitle file) yields a set of sentences together with its ASS style table, carried only by ASS/SSA sources.
/// Passed between operators in-process only and never written into the IR.
/// </summary>
public sealed class SubtitleSourceItem
{
    /// <summary>Source identifier; written into each sentence's <see cref="Sentence.Source"/>, such as "video.mkv #track 2 (eng)" or "chs.ass".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Sentences parsed from this source; Source is not yet marked here and is filled uniformly by the merge operator.</summary>
    public List<Sentence> Sentences { get; init; } = [];

    /// <summary>ASS style table carried by this source; empty for non-ASS inputs.</summary>
    public List<AssStyle> Styles { get; init; } = [];
}
