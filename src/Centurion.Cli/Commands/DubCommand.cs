using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Tts;
using Centurion.Abstractions.Utils;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Capabilities.Infrastructure.Tts;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>dub</c> command: media dubbing — takes a Centurion intermediate file
/// (sentences/translations/speakers) and outputs a dubbed wav plus an intermediate file.
/// Phase 1 MVP pipeline: speaker profiling → TTS synthesis (Qwen3-TTS via llama-tts)
/// → time alignment → mixing → quality report. The TTS engine is chosen at assembly time
/// (llama / indextts / qora) and injected into the synthesis operator; the operator itself
/// holds no runtime engine switch. Tools and models auto-download on demand
/// (llama.cpp / Qwen3-TTS GGUF).
/// </summary>
public sealed class DubCommand(
    ITempDirectoryManager tempManager,
    SpeakerProfilingOperator speakerProfilingOp,
    TimeAlignmentOperator timeAlignmentOp,
    AudioMixOperator audioMixOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<DubCommand> logger, ICenturionDocumentStore store) : AsyncCommand<DubSettings>
{
    /// <summary>
    /// Runs the dubbing flow: assembles and runs the dub pipeline, writing the dubbed wav.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The dub command settings.</param>
    /// <param name="ct">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, DubSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);

            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, "dub");
            var wavPath = Path.ChangeExtension(inputPath, ".dub.wav");
            var mediaPath = settings.MediaFile?.FullName;
            if (!string.IsNullOrWhiteSpace(mediaPath) && !File.Exists(mediaPath))
                throw new FileNotFoundException($"Media file not found: {mediaPath}", mediaPath);

            // Load the intermediate file, including sentences, translations, and speaker information.
            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };

            // Update dubbing-related configuration fields.
            var previous = workflowContext.Config;
            workflowContext.Config = new WorkflowConfig
            {
                CommandName = "dub",
                InputFilePath = mediaPath ?? previous.InputFilePath ?? inputPath,
                SubtitleFilePath = previous.SubtitleFilePath ?? inputPath,
                OutputFilePath = outputPath,
                TtsEngine = settings.TtsEngine,
                TtsModel = settings.TtsModel,
                TtsLanguage = settings.TargetLanguage,
                SpeakerReferenceDir = settings.SpeakerReference?.FullName,
                DubStrictTiming = settings.StrictTiming,
                DubBackgroundPath = settings.BackgroundFile?.FullName,
                DubLoudnessTarget = settings.LoudnessTarget,
                TtsParallelism = Math.Max(1, settings.TtsParallelism),
                DubMaxChunkSeconds = Math.Max(5, settings.MaxChunkSeconds),
                DubDucking = !settings.NoDucking,
                ShowSpeakerLabels = previous.ShowSpeakerLabels,
                Language = previous.Language,
                CacheDirectory = previous.CacheDirectory ?? "./cache"
            };

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("dub_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            workflowContext.State.DubOutputWavPath = wavPath;

            // Keep the user-provided speaker reference directory outside the temporary directory.
            // Strategy resolution at assembly time: pick the TTS engine from the settings and inject it
            // into the synthesis operator (the operator no longer switches engines at runtime).
            var ttsSynthesisOp = CreateTtsSynthesisOperator(serviceProvider, settings.TtsEngine);
            logger.LogInformation("TTS engine resolved at assembly time: {Engine}", settings.TtsEngine ?? "llama");
            // Dub DAG: profiling, synthesis, alignment, mixing, and quality reporting; pipeline-graph uses the same assembly.
            var dag = BuildDubDag(speakerProfilingOp, ttsSynthesisOp, timeAlignmentOp, audioMixOp, qualityReportOp);

            // --dry-run previews the DAG, models, and costs without running operators.
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, workflowContext.Config, serviceProvider, settings.Json, ct);

            await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);

            var segments = workflowContext.State.DubSegments;
            var dubbed = segments.Count(s => !s.Skipped);

            // Save the intermediate file with dubbing segments and write the dubbed WAV.
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "dub", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);
            if (segments.Count > 0)
            {
                var mixerOutput = workflowContext.State.DubOutputWavPath;
                var finalWav = mixerOutput ?? wavPath;
                if (File.Exists(finalWav) && !string.Equals(finalWav, wavPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(finalWav, wavPath, true);
            }

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Dubbed audio completed: {0}", wavPath));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "dub",
                    status = "ok",
                    input = inputPath,
                    output = wavPath,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Synthesized {0}/{1} segments -> {2}", dubbed, segments.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", outputPath));

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Dubbing pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Resolves the TTS engine once at pipeline-assembly time ("indextts" -> IndexTTS-Rust, "qora" -> Qora,
    /// anything else -> llama-tts) and creates the synthesis operator with that engine injected.
    /// Strategy and operator are fused here: the operator holds no runtime engine switch anymore.
    /// </summary>
    /// <param name="serviceProvider">Container resolving the concrete engine implementations.</param>
    /// <param name="engineName">Engine name from settings; null/empty means the llama default.</param>
    internal static TtsSynthesisOperator CreateTtsSynthesisOperator(IServiceProvider serviceProvider, string? engineName)
    {
        var name = engineName?.Trim().ToLowerInvariant() ?? "llama";
        ITtsEngine engine = name switch
        {
            "indextts" => serviceProvider.GetRequiredService<IndexTtsEngine>(),
            "qora" => serviceProvider.GetRequiredService<QoraTtsEngine>(),
            _ => serviceProvider.GetRequiredService<LlamaTtsEngine>()
        };
        return ActivatorUtilities.CreateInstance<TtsSynthesisOperator>(serviceProvider, engine);
    }

    /// <summary>
    /// Assembles the dub DAG (single source of truth shared with the pipeline graph command):
    /// speaker profiling → TTS synthesis → time alignment → mixing → quality report.
    /// </summary>
    internal static PipelineDag BuildDubDag(
        SpeakerProfilingOperator speakerProfilingOp,
        TtsSynthesisOperator ttsSynthesisOp,
        TimeAlignmentOperator timeAlignmentOp,
        AudioMixOperator audioMixOp,
        QualityReportOperator qualityReportOp)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Speaker Profiling", speakerProfilingOp, description: "Extract speaker profiles")
            .Add("TTS Synthesis", ttsSynthesisOp, dependsOn: ["Speaker Profiling"], description: "Per-segment TTS synthesis (Qwen3-TTS)")
            .Add("Time Alignment", timeAlignmentOp, dependsOn: ["TTS Synthesis"], description: "Align synthesized audio with the subtitle timeline")
            .Add("Audio Mix", audioMixOp, dependsOn: ["Time Alignment"], description: "Mixing/loudness/ducking")
            .Add("Quality Report", qualityReportOp, dependsOn: ["Audio Mix"], description: "Quality report wrap-up");
        return builder.Build();
    }
}
