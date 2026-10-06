namespace Centurion.Models.Workflow;

/// <summary>
/// Information about a single track in a media file, parsed from mkvmerge -i.
/// </summary>
public sealed class MkvTrackInfo
{
    /// <summary>Track ID as reported in the mkvmerge output.</summary>
    public int TrackId { get; init; }

    /// <summary>Track type: video / audio / subtitles.</summary>
    public string Type { get; init; } = "";

    /// <summary>Track codec, such as V_MPEG4/ISO/AVC or S_TEXT/UTF8.</summary>
    public string Codec { get; init; } = "";

    /// <summary>Track language, such as eng or und; null when absent.</summary>
    public string? Language { get; init; }

    /// <summary>Track name, such as "English"; null when absent.</summary>
    public string? Name { get; init; }

    /// <summary>Whether this is a subtitle track.</summary>
[System.Text.Json.Serialization.JsonIgnore]
    public bool IsSubtitle => Type.Equals("subtitles", StringComparison.OrdinalIgnoreCase);

    /// <summary>A concise human-readable description, used in warning messages.</summary>
[System.Text.Json.Serialization.JsonIgnore]
    public string Summary =>
        $"ID {TrackId} ({Codec})" + (Language is null ? "" : $", lang {Language}");
}

/// <summary>
/// Subtitle track check result: the track probing conclusion that mkvtoolnix (mkvmerge -i) produced for the input media.
/// </summary>
public sealed class SubtitleTrackCheckResult
{
    /// <summary>Path of the media file that was checked.</summary>
    public string SourceFile { get; init; } = "";

    /// <summary>Whether mkvmerge ran successfully and parsed the tracks.</summary>
    public bool Checked { get; init; }

    /// <summary>Whether the media contains any subtitle tracks.</summary>
    public bool HasSubtitleTracks { get; init; }

    /// <summary>All tracks in the media.</summary>
    public List<MkvTrackInfo> AllTracks { get; init; } = [];

    /// <summary>The subset of subtitle tracks in the media.</summary>
    public List<MkvTrackInfo> SubtitleTracks { get; init; } = [];

    /// <summary>Explanation when the check was skipped or failed, such as mkvmerge being missing or a non-container file.</summary>
    public string? Message { get; init; }
}
