using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Factories;
using Centurion.Core.Infrastructure;
using Centurion.Core.Managers;
using Centurion.Models.Metadata;
using Centurion.Core.Operators;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.SpellCheck;
using Centurion.Core.Strategy.Diarization;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Strategy.Transcribe;
using Centurion.Core.Update;
using Centurion.Core.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.DependencyInjection;

/// <summary>
/// Centurion.Core 服务注册扩展方法。
/// 将基础设施、策略工厂、管道算子等核心服务的 DI 注册集中于此，
/// 使 CLI 入口保持精简。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Centurion.Core 的全部核心服务。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="metadataPath">
    /// 可选：元数据外部 JSON 路径。不传时按
    /// 环境变量 <c>CENTURION_METADATA_PATH</c> → 默认候选路径
    /// （可执行目录 / 当前目录下的 config\metadata.json）解析。
    /// </param>
    public static IServiceCollection AddCenturionCore(this IServiceCollection services, string? metadataPath = null)
    {
        // ---------- 0. 元数据注册表（程序启动时从外部 JSON 加载） ----------
        var metadata = MetadataJsonLoader.LoadOrDefault(metadataPath);
        services.AddSingleton(metadata.Tools);
        services.AddSingleton(metadata.Models);

        // ---------- 1. 基础设施 ----------
        services.AddSingleton<IBinaryLocator, BinaryLocator>();
        services.AddSingleton<IDeviceDetector, DeviceDetector>();
        services.AddSingleton<ITempDirectoryManager, TempDirectoryManager>();
        services.AddSingleton<IModelPathResolver, ModelPathResolver>();
        services.AddSingleton<Centurion.Core.Operators.Downloader>();

        // ---------- 2. 进程 / 工具 / FFmpeg 管理 ----------
        services.AddTransient<ProcessManager>();
        services.AddSingleton<EncoderfileManager>();
        services.AddSingleton<FFmpegManager>();
        services.AddSingleton<MkvToolNixChecker>();
        services.AddSingleton<MkvtoolnixManager>();
        services.AddTransient<QualityReportOperator>();
        services.AddSingleton<LlamaTtsManager>();
        services.AddSingleton<Centurion.Abstractions.Tts.ITtsEngine, Centurion.Core.Tts.LlamaTtsEngine>();
        services.AddTransient<BilingualSubtitleParserOperator>();
        services.AddTransient<SpeakerProfilingOperator>();
        services.AddTransient<TtsSynthesisOperator>();
        services.AddTransient<TimeAlignmentOperator>();
        services.AddTransient<AudioMixOperator>();
        services.AddSingleton<MediaSubtitleExtractor>();
        services.AddSingleton<HunspellSpellChecker>();

        // ---------- 3. 策略工厂 ----------
        services.AddSingleton<ITranscriptionStrategyFactory, TranscriptionStrategyFactory>();
        services.AddSingleton<ISentenceSplitStrategyFactory, SentenceSplitStrategyFactory>();
        services.AddSingleton<IAlignmentStrategyFactory, AlignmentStrategyFactory>();
        services.AddSingleton<IDiarizationStrategyFactory, DiarizationStrategyFactory>();
        services.AddSingleton<IToolManagerFactory, ToolManagerFactory>();
        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();

        // ---------- 4. 转录策略（具体实现） ----------
        services.AddTransient<WhisperCppStrategy>();
        services.AddTransient<CrispAsrQwenStrategy>();
        services.AddTransient<CrispAsrWhisperStrategy>();

        // ---------- 4b. 说话人分割策略 ----------
        services.AddTransient<CrispAsrDiarizationStrategy>();
        services.AddTransient<PyannoteTitaNetDiarizationStrategy>();

        // ---------- 5. 分句策略 ----------
        services.AddTransient<AggressiveRuleSplitStrategy>();
        services.AddTransient<PassiveRuleSplitStrategy>();

        // ---------- 6. 管道算子 ----------
        services.AddTransient<SubtitleTrackCheckerOperator>();
        services.AddTransient<SpellCheckOperator>();
        services.AddTransient<FFmpegConvertOperator>();
        services.AddTransient<AudioPreprocessOperator>();
        services.AddTransient<VocalSeparationOperator>();
        services.AddTransient<TranscribeOperator>();
        services.AddTransient<DiarizationOperator>();
        services.AddTransient<SentenceSplitOperator>();
        services.AddTransient<TextPreprocessingOperator>();
        services.AddTransient<AlignmentOperator>();
        services.AddTransient<ScriptLoaderOperator>();
        services.AddTransient<ScriptTimelineMapperOperator>();
        services.AddTransient<SubtitleTextCorrectorOperator>();
        services.AddTransient<OverlapResolutionOperator>();
        services.AddTransient<CorrectionReportOperator>();

        // ---------- 转换管道专用算子（使用 SubtitlesParserV2） ----------
        services.AddTransient<ConvertParseOperator>();

        // ---------- 7. 管道执行器 ----------
        services.AddSingleton<PipelineExecutor>();

        // ---------- 8. 转换管道算子序列工厂 ----------
        services.AddTransient<Func<IEnumerable<IPipelineOperator>>>(sp => () =>
        [
            sp.GetRequiredService<ConvertParseOperator>()
        ]);

        // ---------- 9. 自更新服务（GitHub Releases） ----------
        services.AddSingleton<IUpdateService, GitHubUpdateService>();

        return services;
    }
}
