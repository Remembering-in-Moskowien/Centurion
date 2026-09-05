// File: Program.cs
using System.Globalization;
using Centurion.Cli.Console;
using Centurion.Cli.Commands;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Factories;
using Centurion.Core.Managers;
using Centurion.Core.Operators;
using Centurion.Core.PipeLine;
using Centurion.Core.Strategy.Alignment;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Strategy.Transcribe;
using Centurion.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

// ----- Console setup -----
ConsoleServices.Output = new SpectreConsoleOutput();
ConsoleServices.Progress = new SpectreProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

const string version = "alpha";
AnsiConsole.Write(new FigletText($"Centurion {version}") { Color = Color.Yellow });

// Cancellation token
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Cancellation requested...");
};

// ----- DI Container -----
var services = new ServiceCollection();
services.AddLogging();

// ============================================================
// 1. Infrastructure
// ============================================================
services.AddSingleton<IBinaryLocator, BinaryLocator>();
services.AddSingleton<ITempDirectoryManager, TempDirectoryManager>();
services.AddSingleton<IModelPathResolver, ModelPathResolver>();
services.AddSingleton<Centurion.Core.Operators.Downloader>();

// ============================================================
// 2. Process manager (transient)
// ============================================================
services.AddTransient<ProcessManager>();

// ============================================================
// 3. Strategy factories (singleton)
// ============================================================
services.AddSingleton<ITranscriptionStrategyFactory, TranscriptionStrategyFactory>();
services.AddSingleton<ISentenceSplitStrategyFactory, SentenceSplitStrategyFactory>();
services.AddSingleton<IAlignmentStrategyFactory, AlignmentStrategyFactory>();

// ============================================================
// 4. Transcription strategies (concrete implementations)
// ============================================================
services.AddTransient<WhisperCppStrategy>();
services.AddTransient<CrispAsrQwenStrategy>();
services.AddTransient<CrispAsrWhisperStrategy>();

// ============================================================
// 5. Sentence splitting strategies
// ============================================================
services.AddTransient<RuleBasedSplitStrategy>();

// ============================================================
// 6. Pipeline operators (transient)
// ============================================================
services.AddTransient<FFmpegConvertOperator>();
services.AddTransient<TranscribeOp>();
services.AddTransient<SentenceSplitOperator>();
services.AddTransient<TextPreprocessingOp>();
services.AddTransient<AlignmentOp>();

// ---------- 转换管道专用算子（使用 SubtitlesParserV2） ----------
services.AddTransient<ConvertParseOp>();

// ============================================================
// 7. Alignment strategy (no singleton registration - factory handles creation)
// ============================================================

// ============================================================
// 8. Other helper services
// ============================================================
services.AddSingleton<FFmpegManager>();

// ============================================================
// 9. Pipeline executor (singleton)
// ============================================================
services.AddSingleton<PipelineExecutor>();

// ---------- 转换管道算子序列工厂 ----------
services.AddTransient<Func<IEnumerable<IPipelineOperator>>>(sp => () =>
[
    sp.GetRequiredService<ConvertParseOp>()
]);

// ============================================================
// 10. Build service provider
// ============================================================
_ = services.BuildServiceProvider();

// ============================================================
// 11. Configure Spectre.Cli
// ============================================================
var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
    config.AddCommand<ConvertCommand>("convert");
});

// ============================================================
// 12. Run
// ============================================================
return await app.RunAsync(args);