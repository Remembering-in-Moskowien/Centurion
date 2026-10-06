using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Workflow.DependencyInjection;
using Centurion.Core.Workflow.Factories;
using Centurion.Core.Workflow.Strategy.SentenceSplit;
using Centurion.Models;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Centurion.Tests.Core;

public sealed class SaTSplitStrategyTests
{
    private static string TokenizerFixtureFolder() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sat", "xlmr");

    // ---------- Registry ----------

    [Fact]
    public void Registry_SatModels_ContainsSat3LSm()
    {
        var registry = ModelRegistry.Default;

        Assert.True(registry.SatModels.TryGetValue("sat-3l-sm", out var meta));
        Assert.Contains("model.onnx", meta.Files!);
        Assert.Contains("config.json", meta.Files!);
        Assert.Contains("sentencepiece.bpe.model", meta.Files!);
        Assert.Contains("tokenizer.json", meta.Files!);
        Assert.NotNull(meta.FileUrls);
        Assert.Contains("sentencepiece.bpe.model", meta.FileUrls.Keys);
        Assert.Contains("tokenizer.json", meta.FileUrls.Keys);
    }

    // ---------- Tokenizer (real XLM-R BPE fixtures) ----------

    [Fact]
    public void Tokenizer_English_PiecesMatchXlmR()
    {
        var tokenizer = XlmRBpeTokenizer.Load(TokenizerFixtureFolder());

        var (ids, pieces, offsets) = tokenizer.Encode("Hello world. This is a test.");

        Assert.Equal(new[] { "▁Hello", "▁world", ".", "▁This", "▁is", "▁a", "▁test", "." }, pieces);
        Assert.Equal(0, ids[0]);       // <s>
        Assert.Equal(2, ids[^1]);      // </s>
        Assert.Equal(pieces.Length + 2, ids.Length);
    }

    [Fact]
    public void Tokenizer_Chinese_PiecesCoverText()
    {
        var tokenizer = XlmRBpeTokenizer.Load(TokenizerFixtureFolder());

        var text = "你好，世界。这是一个测试。";
        var (ids, pieces, offsets) = tokenizer.Encode(text);

        Assert.NotEmpty(pieces);
        Assert.Equal(0, ids[0]);
        Assert.Equal(2, ids[^1]);
        // Offsets are monotonic and cover the whole text (last piece ends at text length).
        for (var i = 1; i < offsets.Length; i++)
            Assert.True(offsets[i] >= offsets[i - 1]);
        Assert.True(offsets[^1] <= text.Length);
        Assert.NotEqual(3, ids[1]); // 你 is in the vocab, so it must not map to <unk>
    }

    [Fact]
    public void Tokenizer_NoUnknownForAscii()
    {
        var tokenizer = XlmRBpeTokenizer.Load(TokenizerFixtureFolder());
        var (ids, _, _) = tokenizer.Encode("We choose to go to the moon.");
        Assert.DoesNotContain(3, ids); // <unk> must not appear for plain ASCII
    }

    // ---------- Factory ----------

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCenturionCore();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Factory_Sat_ReturnsStrategy()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ISentenceSplitStrategyFactory>();

        var strategy = factory.Create("sat", new SplitOptions { ModelName = "sat-3l-sm", Threshold = 0.5 });

        Assert.IsType<SaTSplitStrategy>(strategy);
    }

    [Fact]
    public void Factory_Sat_UnknownModel_Throws()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ISentenceSplitStrategyFactory>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            factory.Create("sat", new SplitOptions { ModelName = "sat-99x" }));

        Assert.Contains("sat-3l-sm", ex.Message);
    }

    [Fact]
    public void Factory_Wtpsplit_Alias_ReturnsStrategy()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ISentenceSplitStrategyFactory>();

        var strategy = factory.Create("wtpsplit", new SplitOptions());

        Assert.IsType<SaTSplitStrategy>(strategy);
    }

    // ---------- Strategy fallback (no model installed) ----------

    [Fact]
    public async Task Split_WithoutModel_FallsBackToLengthSplit()
    {
        var temp = Path.Combine(Path.GetTempPath(), "centurion-sat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var meta = new ModelMeta("https://example.invalid/sat", ["model.onnx"], "token_classification");
            // Use a model name that is not installed anywhere, so the fallback path is exercised
            // (sat-3l-sm may exist under the test output directory after a real-model run).
            var dict = new Dictionary<string, ModelMeta> { ["sat-fake"] = meta };
            var manager = new ModelManager("sat-fake", dict, BuildProvider(), "sat");

            var ensureEx = await Record.ExceptionAsync(() => manager.EnsureInstalledAsync());
            Assert.True(ensureEx != null, "EnsureInstalledAsync should throw on invalid URL");

            var strategy = new SaTSplitStrategy(manager, 0.5);

            var words = new List<Word>
            {
                new() { Speaker = "", Text = "Hello", Start = 0.0, End = 0.3 },
                new() { Speaker = "", Text = "world,", Start = 0.3, End = 0.6 },
                new() { Speaker = "", Text = "this", Start = 0.6, End = 0.9 },
                new() { Speaker = "", Text = "is", Start = 0.9, End = 1.1 },
                new() { Speaker = "", Text = "a", Start = 1.1, End = 1.3 },
                new() { Speaker = "", Text = "test.", Start = 1.3, End = 1.7 }
            };

            var result = await strategy.Split(words, new SplitOptions { MaxWordsPerLine = 3 });

            Assert.NotEmpty(result);
            Assert.True(result.Count >= 2, $"Expected >= 2 fallback sentences, got {result.Count}.");
            Assert.All(result, s => Assert.False(string.IsNullOrWhiteSpace(s.Text)));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // ---------- Real-model inference (runs when the model is installed) ----------

    /// <summary>
    /// End-to-end SaT inference test: runs only when the sat-3l-sm model has been installed under the
    /// CLI output directory (models/sat/sat-3l-sm, e.g. after 'Centurion models install sat-3l-sm').
    /// Skips silently otherwise; the model manager is pointed at the CLI directory so no model is
    /// downloaded into the test output directory.
    /// </summary>
    [Fact]
    public async Task Split_RealModel_SplitsOnSentenceEnds()
    {
        var cliModels = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Centurion.Cli", "bin", "Debug", "net10.0", "models");
        var modelFolder = Path.Combine(cliModels, "sat", "sat-3l-sm");
        if (!File.Exists(Path.Combine(modelFolder, "model.onnx")))
            return; // model not installed; covered by the fallback tests above

        var meta = ModelRegistry.Default.SatModels["sat-3l-sm"];
        var dict = new Dictionary<string, ModelMeta> { ["sat-3l-sm"] = meta };
        var manager = new ModelManager("sat-3l-sm", dict, BuildProvider(), "sat", cliModels);
        var strategy = new SaTSplitStrategy(manager, 0.5);

        var words = new List<Word>
        {
            new() { Speaker = "", Text = "We", Start = 0.0, End = 0.2 },
            new() { Speaker = "", Text = "choose", Start = 0.2, End = 0.5 },
            new() { Speaker = "", Text = "to", Start = 0.5, End = 0.7 },
            new() { Speaker = "", Text = "go", Start = 0.7, End = 0.9 },
            new() { Speaker = "", Text = "to", Start = 0.9, End = 1.1 },
            new() { Speaker = "", Text = "the", Start = 1.1, End = 1.3 },
            new() { Speaker = "", Text = "moon.", Start = 1.3, End = 1.7 },
            new() { Speaker = "", Text = "It", Start = 1.7, End = 1.9 },
            new() { Speaker = "", Text = "will", Start = 1.9, End = 2.1 },
            new() { Speaker = "", Text = "not", Start = 2.1, End = 2.3 },
            new() { Speaker = "", Text = "be", Start = 2.3, End = 2.5 },
            new() { Speaker = "", Text = "easy.", Start = 2.5, End = 2.9 }
        };

        var result = await strategy.Split(words, new SplitOptions { MaxLength = 120, MaxDuration = 30 });

        Assert.NotEmpty(result);
        Assert.True(result.Count >= 2, $"Expected at least two sentences, got {result.Count}.");
        Assert.Equal("We choose to go to the moon.", result[0].Text);
        Assert.Equal("It will not be easy.", result[1].Text);
    }
}
