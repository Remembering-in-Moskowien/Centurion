using Centurion.Core.Utils;
using Centurion.Models;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests;

/// <summary>
/// Centurion 中间文件（*.centurion.json）读写往返测试：
/// 完整上下文（配置 + 各阶段句子/词级时间戳/说话人/翻译/译制分段 + 诊断）保存后必须无损恢复。
/// </summary>
public sealed class CenturionFileIOTests
{
    private static readonly string TempDir =
        Path.Combine(Path.GetTempPath(), $"centurion_io_test_{Guid.NewGuid():N}");

    private static SubtitleWorkflowContext CreateSampleContext()
    {
        var config = new WorkflowConfig
        {
            CommandName = "spawn",
            InputFilePath = "sample.mp4",
            OutputFilePath = "sample.centurion.json",
            Language = "en",
            TranscriberEngine = "crispasr",
            TranscriberModel = "qwen3-asr-1.7b",
            VocalSeparation = true,
            VocalSeparationModel = "htdemucs",
            ShowSpeakerLabels = true,
            CacheDirectory = "./cache"
        };

        var context = new SubtitleWorkflowContext(config);
        context.State.IsTranscribed = true;
        context.State.IsSplit = true;
        context.State.IsDiarized = true;
        context.State.IsAligned = true;
        context.State.Warnings.Add("sample warning");
        context.State.CurrentSentences =
        [
            new Sentence
            {
                Text = "Hello world.",
                Start = 0.0,
                End = 1.2,
                Words =
                [
                    new Word { Text = "Hello", Start = 0.0, End = 0.5, Speaker = "speaker 0", Status = MappingStatus.Matched },
                    new Word { Text = "world.", Start = 0.5, End = 1.2, Speaker = "speaker 0", Status = MappingStatus.Matched }
                ]
            },
            new Sentence
            {
                Text = "Goodbye now.",
                TranslatedText = "再见。",
                Start = 2.0,
                End = 3.0,
                Words =
                [
                    new Word { Text = "Goodbye", Start = 2.0, End = 2.5, Speaker = "speaker 1", Status = MappingStatus.Matched }
                ]
            }
        ];

        // 译制分段（新增正式字段）
        context.State.DubSegments =
        [
            new DubSegment
            {
                SpeakerId = "speaker 0",
                Text = "你好世界。",
                TargetStartMs = 0,
                TargetEndMs = 1200,
                AlignmentTempo = 1.1,
                Skipped = false
            }
        ];

        return context;
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsFullContext()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "sample.centurion.json");
            var context = CreateSampleContext();

            await CenturionFileIO.SaveAsync(context, path, "spawn", CancellationToken.None);

            var loaded = await CenturionFileIO.LoadAsync(path, CancellationToken.None);

            // 配置
            Assert.Equal("spawn", loaded.Config.CommandName);
            Assert.Equal("crispasr", loaded.Config.TranscriberEngine);
            Assert.Equal("qwen3-asr-1.7b", loaded.Config.TranscriberModel);
            Assert.True(loaded.Config.VocalSeparation);
            Assert.Equal("htdemucs", loaded.Config.VocalSeparationModel);

            // 状态标志与诊断
            Assert.True(loaded.State.IsTranscribed);
            Assert.True(loaded.State.IsAligned);
            Assert.Single(loaded.State.Warnings);
            Assert.Equal("sample warning", loaded.State.Warnings[0]);

            // 句子 + 词级时间戳 + 说话人 + 翻译
            Assert.Equal(2, loaded.State.CurrentSentences.Count);
            var first = loaded.State.CurrentSentences[0];
            Assert.Equal("Hello world.", first.Text);
            Assert.Equal(0.0, first.Start);
            Assert.Equal(1.2, first.End);
            Assert.Equal(2, first.Words.Count);
            Assert.Equal("Hello", first.Words[0].Text);
            Assert.Equal("speaker 0", first.Words[0].Speaker);
            Assert.Equal(MappingStatus.Matched, first.Words[0].Status);
            Assert.Equal("再见。", loaded.State.CurrentSentences[1].TranslatedText);

            // 译制分段
            Assert.Single(loaded.State.DubSegments);
            Assert.Equal("你好世界。", loaded.State.DubSegments[0].Text);
            Assert.Equal("speaker 0", loaded.State.DubSegments[0].SpeakerId);
            Assert.Equal(0, loaded.State.DubSegments[0].TargetStartMs);
            Assert.Equal(1.1, loaded.State.DubSegments[0].AlignmentTempo);
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CenturionFileIO.LoadAsync(Path.Combine(TempDir, "nope.centurion.json"), CancellationToken.None));
    }

    [Fact]
    public async Task Load_NonCenturionJson_Throws()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "junk.json");
            await File.WriteAllTextAsync(path, "{ \"not\": \"centurion\" }");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CenturionFileIO.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("video.mp4", null, "video.centurion.json")]
    [InlineData("subs.srt", null, "subs.centurion.json")]
    [InlineData("movie.centurion.json", null, "movie.centurion.json")]
    [InlineData("movie.centurion.json", "corrected", "movie.corrected.centurion.json")]
    [InlineData("movie.centurion.json", "translated", "movie.translated.centurion.json")]
    [InlineData("movie.centurion.json", "dub", "movie.dub.centurion.json")]
    public void DefaultOutputPath_ProducesExpectedNames(string input, string? suffix, string expected)
    {
        Assert.Equal(expected, CenturionFileIO.DefaultOutputPath(input, suffix));
    }

    [Fact]
    public void IsCenturionFile_DetectsExtension()
    {
        Assert.True(CenturionFileIO.IsCenturionFile("x.centurion.json"));
        Assert.True(CenturionFileIO.IsCenturionFile("X.CENTURION.JSON"));
        Assert.False(CenturionFileIO.IsCenturionFile("x.ass"));
        Assert.False(CenturionFileIO.IsCenturionFile("x.json"));
    }
}
