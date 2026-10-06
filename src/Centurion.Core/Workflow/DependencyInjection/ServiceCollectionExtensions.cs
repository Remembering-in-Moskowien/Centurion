using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Providers;
using Centurion.Core.Workflow.Factories;
using Centurion.Core.Providers;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Models.Metadata;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Core.Processing.SpellCheck;
using Centurion.Core.Workflow.Strategy.Diarization;
using Centurion.Core.Workflow.Strategy.SentenceSplit;
using Centurion.Core.Workflow.Strategy.Transcribe;
using Centurion.Core.Capabilities.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Infrastructure.Tts;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Operators.Download;
using Centurion.Core.Utils.Infrastructure;
using Centurion.Core.Utils.Serialization;
using Centurion.Core.Utils.Media;
namespace Centurion.Core.Workflow.DependencyInjection;

/// <summary>
/// DI registration extension methods for Centurion.Core.
/// Centralizes the DI registration of core services such as infrastructure, strategy factories, and
/// pipeline operators, keeping the CLI entry point slim.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core services of Centurion.Core.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="metadataPath">
    /// Optional: path to the external metadata JSON. When omitted it is resolved as
    /// the environment variable <c>CENTURION_METADATA_PATH</c> -> the default candidate path
    /// (config\metadata.json under the executable directory / the current directory).
    /// </param>
    public static IServiceCollection AddCenturionCore(this IServiceCollection services, string? metadataPath = null)
    {
        // ---------- 0. Metadata catalog (loaded from external JSON at startup) ----------
        services.AddSingleton<MetadataCatalog>(sp => MetadataJsonLoader.LoadOrDefault(
            metadataPath,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Centurion.Metadata")));
        services.AddSingleton(sp => sp.GetRequiredService<MetadataCatalog>().Tools);
        services.AddSingleton(sp => sp.GetRequiredService<MetadataCatalog>().Models);

        // ---------- 0b. IR document store (read/write/validate/migrate *.centurion.json) ----------
        services.AddSingleton<ICenturionDocumentStore, CenturionDocumentStore>();

        // ---------- 1. Infrastructure ----------
        services.AddSingleton<IBinaryLocator, BinaryLocator>();
        services.AddSingleton<IDeviceDetector, DeviceDetector>();
        services.AddSingleton<ITempDirectoryManager, TempDirectoryManager>();
        services.AddSingleton<IModelPathResolver, ModelPathResolver>();
        services.AddSingleton<Centurion.Core.Operators.Download.Downloader>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });

        // ---------- 2. Process / tool / FFmpeg management ----------
        services.AddTransient<ProcessManager>();
        services.AddSingleton<EncoderfileManager>();
        services.AddSingleton<FFmpegManager>();
        services.AddSingleton<MkvToolNixChecker>();
        services.AddSingleton<MkvtoolnixManager>();
        services.AddSingleton<VideoSubFinderManager>();
        services.AddTransient<QualityReportOperator>();
        services.AddSingleton<LlamaTtsManager>();
        services.AddSingleton<LlamaTtsEngine>();
        services.AddSingleton<Centurion.Abstractions.Tts.ITtsEngine>(sp => sp.GetRequiredService<LlamaTtsEngine>());
        services.AddSingleton<IndexTtsManager>();
        services.AddSingleton<IndexTtsEngine>();
        services.AddSingleton<QoraTtsManager>();
        services.AddSingleton<QoraTtsEngine>();
        services.AddTransient<BilingualSubtitleParserOperator>();
        services.AddTransient<SpeakerProfilingOperator>();
        services.AddTransient<TimeAlignmentOperator>();
        services.AddTransient<AudioMixOperator>();
        services.AddSingleton<MediaSubtitleExtractor>();
        services.AddSingleton<HunspellSpellChecker>();

        // ---------- OCR (ocr command / GLM-OCR) ----------
        services.AddSingleton(sp =>
            new OcrClient(
                new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
                sp.GetRequiredService<ILogger<OcrClient>>()));
        services.AddSingleton<RapidOcrModelManager>();
        services.AddSingleton<RapidOcrEngine>();
        services.AddTransient<OcrExtractOperator>();

        // ---------- 3. Strategy factories ----------
        services.AddSingleton<ITranscriptionStrategyFactory, TranscriptionStrategyFactory>();
        services.AddSingleton<ISentenceSplitStrategyFactory, SentenceSplitStrategyFactory>();
        services.AddSingleton<IAlignmentStrategyFactory, AlignmentStrategyFactory>();
        services.AddSingleton<IDiarizationStrategyFactory, DiarizationStrategyFactory>();
        services.AddSingleton<IToolManagerFactory, ToolManagerFactory>();
        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddTransient<PipelineOperatorFactory>();

        // ---------- 4. Transcription strategies (concrete implementations) ----------
        services.AddTransient<WhisperCppStrategy>();
        services.AddTransient<CrispAsrQwenStrategy>();
        services.AddTransient<CrispAsrWhisperStrategy>();
        services.AddTransient<CloudAsrStrategy>();

        // ---------- 4b. Diarization strategies ----------
        services.AddTransient<PolyVoiceDiarizationStrategy>();
        services.AddTransient<WeSpeakerDiarizationStrategy>();

        // ---------- 4c. Vocal separation engine (native htdemucs ONNX Runtime) ----------
        services.AddTransient<Centurion.Core.Workflow.Strategy.VocalSeparation.HtDemucsOnnxVocalSeparator>();

        // ---------- 5. Sentence-split strategies ----------
        services.AddTransient<AggressiveRuleSplitStrategy>();
        services.AddTransient<PassiveRuleSplitStrategy>();

        // ---------- 6. Pipeline operators ----------
        services.AddTransient<SubtitleTrackCheckerOperator>();
        services.AddTransient<SpellCheckOperator>();
        services.AddTransient<FFmpegConvertOperator>();
        services.AddTransient<AudioPreprocessOperator>();
        services.AddTransient<VocalSeparationOperator>();
        services.AddTransient<TextPreprocessingOperator>();
        services.AddTransient<ScriptLoaderOperator>();
        services.AddTransient<CombineParseOperator>();
        services.AddTransient<CombineMergeOperator>();
        services.AddTransient<CombineDedupeOperator>();
        services.AddTransient<ScriptTimelineMapperOperator>();
        services.AddTransient<SubtitleTextCorrectorOperator>();
        services.AddTransient<OverlapResolutionOperator>();
        services.AddTransient<CorrectionReportOperator>();

        // ---------- Conversion-pipeline-specific operators (use SubtitlesParserV2) ----------
        services.AddTransient<ConvertParseOperator>();

        // ---------- 7. Pipeline executor ----------
        services.AddSingleton<PipelineExecutor>();

        // ---------- 8. Conversion-pipeline operator sequence factory ----------
        services.AddTransient<Func<IEnumerable<IPipelineOperator>>>(sp => () =>
        [
            sp.GetRequiredService<ConvertParseOperator>()
        ]);

        // ---------- 9. Self-update service (GitHub Releases) ----------
                // ---------- 8b. Provider abstraction (unified local/cloud abstraction + fallback chain) ----------
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IProviderFactory, ProviderFactory>();

services.AddSingleton<IUpdateService, GitHubUpdateService>();

        return services;
    }
}
