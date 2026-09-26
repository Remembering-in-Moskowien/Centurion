using Centurion.Core.Utils.Serialization;
using Centurion.Models.Schema;
using Centurion.Models.Workflow;
using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// <see cref="CenturionDocumentStore"/> 专项测试：旧版 meta/config/state 格式兼容与迁移、
/// validate 校验、schemaVersion 严格性。
/// </summary>
public sealed class CenturionDocumentStoreTests
{
    private static readonly string TempDir =
        Path.Combine(Path.GetTempPath(), $"centurion_store_test_{Guid.NewGuid():N}");

    private const string LegacyJson = """
        {
          "meta": {
            "format": "centurion",
            "version": 1,
            "command": "spawn",
            "generatedAt": "2026-09-26T09:47:47.5080819+08:00",
            "inputFile": "D:\\test.mp4",
            "outputFile": "D:\\test.spawn.centurion.json"
          },
          "config": {
            "commandName": "spawn",
            "inputFilePath": "D:\\test.mp4",
            "language": "en",
            "transcriberModel": "qwen3-asr-1.7b"
          },
          "state": {
            "isTranscribed": true,
            "currentSentences": [
              {
                "text": "Hello.",
                "start": 0.0,
                "end": 1.0,
                "words": [
                  { "text": "Hello.", "start": 0.0, "end": 1.0, "speaker": "spk", "status": "Matched" }
                ]
              }
            ],
            "extensions": { "stepTimings": { "x": "00:00:01" } }
          }
        }
        """;

    private async Task<string> WriteLegacyAsync()
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, "legacy.centurion.json");
        await File.WriteAllTextAsync(path, LegacyJson);
        return path;
    }

    [Fact]
    public async Task Load_LegacyFormat_IsCompatible()
    {
        var path = await WriteLegacyAsync();
        try
        {
            var store = new CenturionDocumentStore();
            var doc = await store.LoadAsync(path, CancellationToken.None);

            Assert.Equal(CenturionSchema.CurrentVersion, doc.SchemaVersion);
            Assert.Equal("spawn", doc.Generator.Command);
            Assert.Equal("D:\\test.mp4", doc.Generator.InputFile);
            Assert.Equal("en", doc.Config.Language);
            Assert.Equal("qwen3-asr-1.7b", doc.Config.TranscriberModel);
            Assert.True(doc.State.IsTranscribed);
            Assert.Single(doc.State.CurrentSentences);
            Assert.Equal("Hello.", doc.State.CurrentSentences[0].Text);
            Assert.Equal("spk", doc.State.CurrentSentences[0].Words[0].Speaker);
            // Extensions 不持久化：旧文件的 extensions 被丢弃
            Assert.Empty(doc.State.Extensions);
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_LegacyToCurrent_MapsMetaFields()
    {
        var path = await WriteLegacyAsync();
        try
        {
            var store = new CenturionDocumentStore();
            var doc = await store.MigrateAsync(path, CenturionSchema.CurrentVersion, CancellationToken.None);

            Assert.Equal(CenturionSchema.CurrentVersion, doc.SchemaVersion);
            Assert.Equal("spawn", doc.Generator.Command);
            Assert.Equal("2026-09-26T09:47:47.5080819+08:00", doc.Generator.GeneratedAt);
            Assert.Equal("D:\\test.mp4", doc.Generator.InputFile);
            Assert.Equal("D:\\test.spawn.centurion.json", doc.Generator.OutputFile);
            Assert.NotNull(doc.Generator.Version);
            Assert.NotEqual(string.Empty, doc.Generator.Version);
            Assert.Equal("en", doc.Config.Language);
            Assert.True(doc.State.IsTranscribed);
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_InvalidJson_ReportsIssue()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "bad.json");
            await File.WriteAllTextAsync(path, "{ not json");
            var store = new CenturionDocumentStore();
            var result = await store.ValidateAsync(path, CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Contains("JSON", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_MissingSchemaVersion_ReportsIssue()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "nope.json");
            await File.WriteAllTextAsync(path, "{ \"not\": \"centurion\" }");
            var store = new CenturionDocumentStore();
            var result = await store.ValidateAsync(path, CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Contains("schemaVersion", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_UnsupportedVersion_ReportsIssue()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "future.json");
            await File.WriteAllTextAsync(path,
                """{ "schemaVersion": "99.0", "generator": {}, "config": {}, "state": {} }""");
            var store = new CenturionDocumentStore();
            var result = await store.ValidateAsync(path, CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Contains("99.0", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_LegacyFormat_IsValid()
    {
        var path = await WriteLegacyAsync();
        try
        {
            var store = new CenturionDocumentStore();
            var result = await store.ValidateAsync(path, CancellationToken.None);

            Assert.True(result.IsValid);
            Assert.Equal("legacy", result.Version);
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_UnsupportedVersion_Throws()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var path = Path.Combine(TempDir, "future.json");
            await File.WriteAllTextAsync(path,
                """{ "schemaVersion": "99.0", "generator": {}, "config": {}, "state": {} }""");
            var store = new CenturionDocumentStore();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }


    [Fact]
    public async Task Save_FormalDubFieldsPersist_RuntimeAndExtensionsDoNot()
    {
        Directory.CreateDirectory(TempDir);
        try
        {
            var store = new CenturionDocumentStore();
            var doc = new CenturionDocument
            {
                SchemaVersion = CenturionSchema.CurrentVersion,
                Generator = new GeneratorInfo { Tool = "centurion", Command = "dub" },
                Config = new WorkflowConfig { CommandName = "dub" },
                State = new WorkflowState
                {
                    DubOutputWavPath = "out.dub.wav",
                    DubSpeakerReferences = new Dictionary<string, string> { ["SPEAKER_00"] = "ref0.wav" },
                    DubSegments = [new DubSegment { Text = "Hello." }],
                    // 运行时字段：不写入 IR
                    StepTimings = new Dictionary<string, TimeSpan> { ["TTS"] = TimeSpan.FromSeconds(2) },
                    CorrectionMetadata = new Dictionary<Sentence, Dictionary<string, object>>(),
                    // 临时扩展槽：不写入 IR
                    Extensions = new Dictionary<string, object> { ["SourceAudioInfo"] = "probe", ["EstimatedSnrDb"] = 40.0 }
                }
            };

            var outPath = Path.Combine(TempDir, "dub.centurion.json");
            await store.SaveAsync(doc, outPath, CancellationToken.None);
            var json = await File.ReadAllTextAsync(outPath);

            Assert.Contains("\"dubOutputWavPath\"", json);
            Assert.Contains("\"dubSpeakerReferences\"", json);
            Assert.DoesNotContain("\"extensions\"", json);
            Assert.DoesNotContain("\"stepTimings\"", json);
            Assert.DoesNotContain("\"correctionMetadata\"", json);
            Assert.DoesNotContain("\"sourceAudioInfo\"", json);

            var loaded = await store.LoadAsync(outPath, CancellationToken.None);
            Assert.Equal("out.dub.wav", loaded.State.DubOutputWavPath);
            Assert.Equal("ref0.wav", loaded.State.DubSpeakerReferences["SPEAKER_00"]);
            Assert.Single(loaded.State.DubSegments);
            Assert.Empty(loaded.State.Extensions);
            Assert.Empty(loaded.State.StepTimings);
            Assert.Empty(loaded.State.CorrectionMetadata);
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_UnsupportedTarget_Throws()
    {
        var path = await WriteLegacyAsync();
        try
        {
            var store = new CenturionDocumentStore();
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                store.MigrateAsync(path, "99.0", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(TempDir, recursive: true);
        }
    }
}
