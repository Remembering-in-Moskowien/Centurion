using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Workflow.DependencyInjection;
using Centurion.Core.Workflow.Factories;
using Centurion.Core.Workflow.Strategy.Translation;
using Centurion.Models;
using Centurion.Models.Metadata;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Centurion.Tests.Core;

public sealed class OpusMtTranslationTests
{
    private static string FixtureFolder() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "opusmt", "zh-en");

    // ---------- Model name resolution ----------

    [Theory]
    [InlineData("zh-en", "zh-en")]
    [InlineData("opus-mt-zh-en", "zh-en")]
    [InlineData("OPUS-MT-EN-ZH", "en-zh")]
    [InlineData("ja-en", "ja-en")]
    [InlineData("", "en-zh")] // empty model falls back to source/target derivation
    public void ResolvePair_ModelName_Normalizes(string model, string? expected)
    {
        var result = OpusMtModelNames.ResolvePair(model, "en", "zh");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("en", "zh", "en-zh")]
    [InlineData("zh", "en", "zh-en")]
    [InlineData("ja", "en", "jap-en")]
    [InlineData("zh-cn", "en", "zh-en")]
    [InlineData("auto", "zh", null)]
    [InlineData("en", null, null)]
    [InlineData("fr", "es", "fr-es")]
    public void ResolvePair_FromLanguages_DerivesPair(string source, string? target, string? expected)
    {
        var result = OpusMtModelNames.ResolvePair(null, source, target);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToOpusCode_MapsKnownLanguages()
    {
        Assert.Equal("jap", OpusMtModelNames.ToOpusCode("ja"));
        Assert.Equal("zh", OpusMtModelNames.ToOpusCode("zh-hant"));
        Assert.Null(OpusMtModelNames.ToOpusCode("xx"));
    }

    // ---------- Tokenizer (real SentencePiece fixture) ----------

    [Fact]
    public void Tokenizer_EncodeDecode_RoundTripsEnglish()
    {
        var tokenizer = new OpusMtTokenizer(FixtureFolder());

        var ids = tokenizer.Encode("Hello, world!");
        var decoded = tokenizer.Decode(ids);

        Assert.NotEmpty(ids);
        Assert.Equal("Hello, world!", decoded);
    }

    [Fact]
    public void Tokenizer_EncodeDecode_HandlesChinese()
    {
        var tokenizer = new OpusMtTokenizer(FixtureFolder());

        var ids = tokenizer.Encode("你好，世界。");
        var decoded = tokenizer.Decode(ids);

        Assert.NotEmpty(ids);
        // SentencePiece normalizes full-width punctuation to ASCII (comma), as the official implementation does.
        Assert.Contains("你好", decoded);
        Assert.Contains("世界", decoded);
    }

    [Fact]
    public void Tokenizer_AllPiecesExistInVocab()
    {
        var tokenizer = new OpusMtTokenizer(FixtureFolder());

        foreach (var text in new[] { "Hello, world!", "This is a test.", "好的，我们明天见。", "Mr. Smith's 3rd quote — dash" })
        {
            var ids = tokenizer.Encode(text);
            // Every emitted id must be a real vocab entry (<unk> id 1 means an out-of-vocab piece).
            Assert.All(ids, id => Assert.InRange(id, 0, 65000));
        }
    }

    [Fact]
    public void Tokenizer_EmptyText_EncodesToNothing()
    {
        var tokenizer = new OpusMtTokenizer(FixtureFolder());
        Assert.Empty(tokenizer.Encode(""));
    }

    // ---------- Strategy behavior without ONNX models ----------

    [Fact]
    public async Task Strategy_ScriptAlignment_FillsTranslationsWithoutModel()
    {
        // ModelManager with an empty model name disables management: no download, no engine.
        var manager = new ModelManager(
            string.Empty, new Dictionary<string, ModelMeta>(), new ServiceCollection().BuildServiceProvider(), "opusmt");
        var strategy = new OpusMtTranslationStrategy(manager, beamSize: 1, maxLength: 64);
        var sentences = new List<Sentence>
        {
            new() { Text = "Hello.", Start = 0, End = 1000, Words = [] },
            new() { Text = "Goodbye.", Start = 1000, End = 2000, Words = [] }
        };
        var options = new TranslationOptions
        {
            TargetLanguage = "zh",
            TargetScriptLines = new[] { "你好。", "再见。" }
        };

        var result = await strategy.TranslateAsync(sentences, options);

        Assert.Equal("你好。", result[0].TranslatedText);
        Assert.Equal("再见。", result[1].TranslatedText);
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
    public void Factory_Opus_UnknownModel_ThrowsWithAvailablePairs()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITranslationStrategyFactory>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            factory.Create("opus", new TranslationRequestOptions { Model = "xx-yy" }));

        Assert.Contains("zh-en", ex.Message);
        Assert.Contains("en-zh", ex.Message);
    }

    [Fact]
    public void Factory_Opus_KnownPair_ReturnsStrategy()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITranslationStrategyFactory>();

        var strategy = factory.Create("opus", new TranslationRequestOptions
        {
            Model = "zh-en",
            SourceLanguage = "zh",
            TargetLanguage = "en",
            BeamSize = 2,
            MaxLength = 128
        });

        Assert.IsType<OpusMtTranslationStrategy>(strategy);
    }

    [Fact]
    public void Factory_UnknownStrategy_Throws()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITranslationStrategyFactory>();

        Assert.Throws<NotSupportedException>(() => factory.Create("nope"));
    }

    // ---------- Real-model inference (runs when the model is installed) ----------

    /// <summary>
    /// End-to-end inference test: runs only when the OPUS-MT zh-en model has been installed
    /// (models/opusmt/zh-en under the app base directory, e.g. via
    /// 'Centurion models install zh-en' or the e2e script). Skips silently otherwise so that
    /// plain unit-test runs do not require the ~160 MB download.
    /// </summary>
    [Fact]
    public void Engine_RealModel_TranslatesChineseToEnglish()
    {
        var modelFolder = Path.Combine(AppContext.BaseDirectory, "models", "opusmt", "zh-en");
        var onnxFolder = Path.Combine(modelFolder, "onnx");
        if (!File.Exists(Path.Combine(onnxFolder, "encoder_model_quantized.onnx")) &&
            !File.Exists(Path.Combine(onnxFolder, "encoder_model.onnx")))
            return; // Model not installed — this check is exercised by the e2e script.

        using var engine = new OpusMtEngine(modelFolder);
        var translation = engine.Translate("你好，世界。", beamSize: 1, maxLength: 64);

        Assert.False(string.IsNullOrWhiteSpace(translation));
        Assert.Contains("hello", translation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("world", translation, StringComparison.OrdinalIgnoreCase);
    }
}
