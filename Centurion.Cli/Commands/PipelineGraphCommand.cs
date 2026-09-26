using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Factories;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Workflow.Factories;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>pipeline graph</c> 命令：渲染指定命令的 DAG 管线拓扑（节点、依赖、条件、重试/降级标注）。
/// 与 asr / ocr / from-script / translate / dub / correct / convert 命令共享同一 DAG 装配，只渲染不执行。
/// </summary>
public sealed class PipelineGraphCommand(
    SubtitleTrackCheckerOperator trackChecker,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    PipelineOperatorFactory operatorFactory,
    TextPreprocessingOperator textCleaningOp,
    QualityReportOperator qualityReportOp,
    OcrExtractOperator ocrExtractOp,
    SpeakerProfilingOperator speakerProfilingOp,
    TtsSynthesisOperator ttsSynthesisOp,
    TimeAlignmentOperator timeAlignmentOp,
    AudioMixOperator audioMixOp,
    ScriptLoaderOperator scriptLoaderOp,
    SubtitleTextCorrectorOperator textCorrectorOp,
    OverlapResolutionOperator overlapOp,
    CorrectionReportOperator correctionReportOp,
    SpellCheckOperator spellCheckOp,
    ScriptTimelineMapperOperator mapperOp,
    Func<IEnumerable<IPipelineOperator>> convertOperatorsFactory,
    ITranslationStrategyFactory strategyFactory,
    IServiceProvider serviceProvider,
    ILogger<PipelineGraphCommand> logger) : AsyncCommand<PipelineGraphSettings>
{
    /// <summary>执行：渲染指定命令的 DAG 拓扑（控制台或文件）。</summary>
    protected override async Task<int> ExecuteAsync(CommandContext context, PipelineGraphSettings settings, CancellationToken ct)
    {
        try
        {
            var commandName = settings.Command.ToLowerInvariant();
            var dag = commandName switch
            {
                "asr" => SpawnCommand.BuildAsrDag(
                    trackChecker, ffmpegOp, audioPreprocessOp, vocalSepOp,
                    operatorFactory, textCleaningOp, qualityReportOp,
                    FullConfig()),
                "ocr" => OcrCommand.BuildOcrDag(
                    ocrExtractOp, operatorFactory, textCleaningOp, qualityReportOp,
                    FullConfig()),
                "from-script" => FromScriptCommand.BuildFromScriptDag(
                    trackChecker, ffmpegOp, audioPreprocessOp, vocalSepOp,
                    operatorFactory, scriptLoaderOp, textCleaningOp, mapperOp, qualityReportOp,
                    FullConfig()),
                "translate" => BuildTranslateDagForGraph(),
                "dub" => DubCommand.BuildDubDag(
                    speakerProfilingOp, ttsSynthesisOp, timeAlignmentOp, audioMixOp, qualityReportOp),
                "correct" => CorrectCommand.BuildCorrectDag(
                    scriptLoaderOp, textCorrectorOp, ffmpegOp, audioPreprocessOp, vocalSepOp,
                    operatorFactory, overlapOp, spellCheckOp, correctionReportOp, qualityReportOp,
                    FullConfig(), CorrectionStrategy.Both, needsAudio: true, runSpellCheck: true),
                "convert" => ConvertCommand.BuildConvertDag(
                    convertOperatorsFactory().ToList(), qualityReportOp),
                _ => throw new ArgumentException(
                    $"Unknown pipeline command '{settings.Command}'. " +
                    "Supported: asr, ocr, from-script, translate, dub, correct, convert.")
            };

            var validation = dag.Validate();
            if (validation is not null)
                throw new InvalidOperationException(validation);

            var extension = settings.OutputFile?.Extension.ToLowerInvariant() ?? string.Empty;
            if (settings.OutputFile is not null)
            {
                var content = extension switch
                {
                    ".html" => PipelineGraphRenderer.RenderHtml(dag),
                    ".txt" => PipelineGraphRenderer.RenderText(dag),
                    _ => PipelineGraphRenderer.RenderMermaid(dag)
                };
                await File.WriteAllTextAsync(settings.OutputFile.FullName, content, ct);
                ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Pipeline graph written to {0}", settings.OutputFile.FullName));
                return 0;
            }

            ConsoleServices.Output.WriteLine(PipelineGraphRenderer.RenderMermaid(dag));
            ConsoleServices.Output.WriteLine(ConsoleServices.T("— Topology —"));
            ConsoleServices.Output.WriteLine(PipelineGraphRenderer.RenderText(dag));
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "pipeline graph rendering failed.");
            ConsoleServices.Output.WriteError(ex.Message);
            return 1;
        }
    }

    /// <summary>全开配置：展示完整拓扑（含人声分离/说话人分割/对齐条件节点）。</summary>
    private static WorkflowConfig FullConfig() => new()
    {
        VocalSeparation = true,
        DiarizationBackend = "crispasr",
        EnableAlignment = true,
        EnableTextCleaning = true
    };

    /// <summary>translate DAG：以占位配置构建（只渲染，不执行翻译）。</summary>
    private PipelineDag BuildTranslateDagForGraph()
    {
        var options = new Centurion.Abstractions.Strategy.TranslationOptions { TargetLanguage = "zh" };
        var strategy = strategyFactory.Create("llm", new Centurion.Models.Llm.LlmOptions());
        var translationOp = ActivatorUtilities.CreateInstance<TranslationOperator>(serviceProvider, strategy, options);
        return TranslateCommand.BuildTranslateDag(translationOp, qualityReportOp);
    }
}
