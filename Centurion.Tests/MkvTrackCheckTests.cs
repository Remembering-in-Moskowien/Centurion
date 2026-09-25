using Centurion.Core.Utils;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests;

/// <summary>
/// mkvmerge -i 输出解析（字幕轨检查）单元测试。
/// </summary>
public class MkvTrackCheckTests
{
    private const string SampleMkvMergeOutput =
        """
        File 'sample.mkv': container: Matroska
        Track ID 0: video (V_MPEG4/ISO/AVC) [language:und]
        Track ID 1: audio (A_AAC) [language:eng]
        Track ID 2: subtitles (S_TEXT/UTF8) [language:eng, name:English]
        Track ID 3: subtitles (S_TEXT/ASS) [language:chi, name:Chinese]
        """;

    [Fact]
    public void Parse_RecognizesTracksAndAttributes()
    {
        var tracks = MkvToolNixChecker.ParseTrackLines(SampleMkvMergeOutput);

        Assert.Equal(4, tracks.Count);

        Assert.Equal(0, tracks[0].TrackId);
        Assert.Equal("video", tracks[0].Type);
        Assert.Equal("V_MPEG4/ISO/AVC", tracks[0].Codec);
        Assert.Equal("und", tracks[0].Language);
        Assert.False(tracks[0].IsSubtitle);

        Assert.Equal(1, tracks[1].TrackId);
        Assert.Equal("audio", tracks[1].Type);
        Assert.Equal("A_AAC", tracks[1].Codec);

        Assert.Equal(2, tracks[2].TrackId);
        Assert.Equal("subtitles", tracks[2].Type);
        Assert.Equal("S_TEXT/UTF8", tracks[2].Codec);
        Assert.Equal("eng", tracks[2].Language);
        Assert.Equal("English", tracks[2].Name);
        Assert.True(tracks[2].IsSubtitle);
        Assert.Equal("ID 2 (S_TEXT/UTF8), lang eng", tracks[2].Summary);
    }

    [Fact]
    public void Parse_FiltersSubtitleTracks()
    {
        var tracks = MkvToolNixChecker.ParseTrackLines(SampleMkvMergeOutput);
        var subtitles = tracks.Where(t => t.IsSubtitle).ToList();

        Assert.Equal(2, subtitles.Count);
        Assert.All(subtitles, t => Assert.Equal("subtitles", t.Type));
        Assert.Contains(subtitles, t => t.Language == "chi");
    }

    [Fact]
    public void Parse_EmptyOrUnrelatedLines_ReturnsEmpty()
    {
        Assert.Empty(MkvToolNixChecker.ParseTrackLines(""));
        Assert.Empty(MkvToolNixChecker.ParseTrackLines("File 'x.mkv': container: Matroska\nError: no tracks"));
    }

    [Fact]
    public void Parse_HandlesMissingAttributes()
    {
        var tracks = MkvToolNixChecker.ParseTrackLines("Track ID 5: subtitles (S_TEXT/UTF8)");

        var subtitle = Assert.Single(tracks);
        Assert.Equal(5, subtitle.TrackId);
        Assert.True(subtitle.IsSubtitle);
        Assert.Null(subtitle.Language);
        Assert.Null(subtitle.Name);
        Assert.Equal("ID 5 (S_TEXT/UTF8)", subtitle.Summary);
    }
}
