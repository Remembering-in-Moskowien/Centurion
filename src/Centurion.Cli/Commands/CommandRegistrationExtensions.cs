using Centurion.Models.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

internal static class CommandRegistrationExtensions
{
    internal static void ConfigureCenturionCommands(this CommandApp app)
    {
        app.Configure(config =>
        {
            config.SetApplicationName("Centurion");
            // ─── Getting started ───
            config.AddCommand<InitCommand>("init")
                .WithDescription(ConsoleServices.T("Interactive wizard: generates centurion.config.json and a recommended command chain (transcribe → translate → subtitles)"));
            // ─── Core subtitle workflows (all use DAGs; inspect topology with pipeline-graph) ───
            config.AddCommand<SpawnCommand>("asr")
                .WithDescription(ConsoleServices.T("Auto subtitle generation: media → transcribe/diarize/split/align → Centurion intermediate file"));
            config.AddCommand<OcrCommand>("ocr")
                .WithDescription(ConsoleServices.T("Video/image subtitle OCR: frame OCR (RapidOCR/LLM) → split/clean → intermediate file"));
            config.AddCommand<FromScriptCommand>("from-script")
                .WithDescription(ConsoleServices.T("Script alignment: transcribe media against the given script and map the timeline → intermediate file"));
            config.AddCommand<CorrectCommand>("correct")
                .WithDescription(ConsoleServices.T("Subtitle correction: fix text and timeline against reference script/audio (timeline-only/text-only/both)"));
            config.AddCommand<TranslateCommand>("translate")
                .WithDescription(ConsoleServices.T("Subtitle translation: LLM strategy + glossary/target script 1:1 alignment, bilingual output"));
            config.AddCommand<DubCommand>("dub")
                .WithDescription(ConsoleServices.T("Media dubbing: speaker profiling → TTS → time alignment → mixing → dubbed wav"));
            config.AddCommand<ConvertCommand>("convert")
                .WithDescription(ConsoleServices.T("Subtitle conversion: parse ASS/SRT/TXT subtitles → Centurion intermediate file"));
            config.AddCommand<CombineCommand>("combine")
                .WithDescription(ConsoleServices.T("Merge existing subtitle tracks from media files or subtitle inputs into one combined subtitle stream"));
            config.AddCommand<ServeCommand>("serve")
                .WithDescription(ConsoleServices.T("HTTP service: expose packaged commands via POST /commands/{name} (REST)"));
            config.AddCommand<BuildCommand>("build")
                .WithDescription(ConsoleServices.T("Subtitle build: intermediate file → ASS/SRT/TXT subtitles"));
            // ─── Quality and utilities ───
            config.AddCommand<QualityCommand>("quality")
                .WithDescription(ConsoleServices.T("Quality report: .quality.json/.html, --fix auto-repair, --fail-on CI thresholds"));
            config.AddCommand<PipelineGraphCommand>("pipeline-graph")
                .WithDescription(ConsoleServices.T("Pipeline DAG visualization: render a command's topology (no execution, -c selects)"));
            config.AddCommand<ValidateCommand>("validate")
                .WithDescription(ConsoleServices.T("Validate Centurion intermediate files against the IR schema"));
            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription(ConsoleServices.T("Environment diagnostics: probe runtime/toolchain/models/config and write a diagnostic log"));
            config.AddCommand<MigrateCommand>("migrate")
                .WithDescription(ConsoleServices.T("IR schema migration: upgrade older intermediate files to a target version (--to)"));
            // ─── Model registry and provider discovery ───
            config.AddBranch("models", models =>
            {
                models.SetDescription(ConsoleServices.T("Model registry management: list/install/verify/remove local models (whisper/qwen3/diarization/bert/tts)"));
                models.AddCommand<ModelsListCommand>("list")
                    .WithDescription(ConsoleServices.T("List all registered models and local readiness (table + badges)"));
                models.AddCommand<ModelsInstallCommand>("install")
                    .WithDescription(ConsoleServices.T("Download and install the given model (suggested when a run fails on missing models)"));
                models.AddCommand<ModelsVerifyCommand>("verify")
                    .WithDescription(ConsoleServices.T("Verify local model files are ready (exit code 1 when missing)"));
                models.AddCommand<ModelsRemoveCommand>("remove")
                    .WithDescription(ConsoleServices.T("Remove local model files/directories (requires confirmation)"));
            });
            config.AddBranch("providers", providers =>
            {
                providers.SetDescription(ConsoleServices.T("Provider inspection: list/probe local & cloud inference providers (cost/latency/quality)"));
                providers.AddCommand<ProvidersListCommand>("list")
                    .WithDescription(ConsoleServices.T("List all providers with capabilities and availability (table + cost chart)"));
                providers.AddCommand<ProvidersTestCommand>("test")
                    .WithDescription(ConsoleServices.T("Probe a provider's availability and show its capability declaration"));
            });
        });
    }
}