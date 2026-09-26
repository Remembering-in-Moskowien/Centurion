using Centurion.Core.Utils.Serialization;
using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.SchemaGen;

/// <summary>
/// 开发工具：生成 IR 的 JSON Schema（schemas/centurion-v1.json）与样例中间文件
/// （samples/sample.centurion.json）。结构变更后重跑本工具即可同步两处契约产物。
/// 用法：dotnet run --project tools/schema-gen -- [schemaPath] [samplePath]
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var schemaPath = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", "centurion-v1.json");
        var samplePath = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "sample.centurion.json");

        // ---- 1. JSON Schema ----
        var schema = CenturionSchemaExporter.ExportV1();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(schemaPath))!);
        await File.WriteAllTextAsync(schemaPath, schema);
        Console.WriteLine($"Schema written: {Path.GetFullPath(schemaPath)}");

        // ---- 2. 样例中间文件（与 Schema 同一序列化路径生成，保证一致）----
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(samplePath))!);
        var context = CreateSampleContext();
        var doc = CenturionDocumentBuilder.Create(context, "spawn", samplePath);
        var store = new CenturionDocumentStore();
        await store.SaveAsync(doc, samplePath, CancellationToken.None);
        Console.WriteLine($"Sample written: {Path.GetFullPath(samplePath)}");
        return 0;
    }

    /// <summary>构造最小但有代表性的工作流上下文（含一句带词级明细的句子与阶段标志）。</summary>
    private static SubtitleWorkflowContext CreateSampleContext()
    {
        var config = new WorkflowConfig
        {
            CommandName = "spawn",
            InputFilePath = "samples/test.mp4",
            OutputFilePath = "samples/sample.centurion.json",
            Language = "en",
            TranscriberEngine = "crispasr",
            TranscriberModel = "qwen3-asr-1.7b",
            ShowSpeakerLabels = true,
            CacheDirectory = "./cache"
        };

        var context = new SubtitleWorkflowContext(config);
        context.State.IsAudioConverted = true;
        context.State.IsTranscribed = true;
        context.State.IsSplit = true;
        context.State.CurrentSentences =
        [
            new Sentence
            {
                Text = "Welcome to Centurion.",
                Start = 0.0,
                End = 1.6,
                Words =
                [
                    new Word { Text = "Welcome", Start = 0.0, End = 0.6, Speaker = "SPEAKER_00", Status = MappingStatus.Matched },
                    new Word { Text = "to", Start = 0.6, End = 0.8, Speaker = "SPEAKER_00", Status = MappingStatus.Matched },
                    new Word { Text = "Centurion.", Start = 0.8, End = 1.6, Speaker = "SPEAKER_00", Status = MappingStatus.Matched }
                ]
            }
        ];
        return context;
    }
}
