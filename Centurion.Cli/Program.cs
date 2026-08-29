using System.Globalization;
using Centurion.Cli.Console;
using Centurion.Cli.Commands;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Operators;
using Centurion.Core.PipeLine;
using Centurion.Core.Strategy.Alignment;
using Centurion.Core.Strategy.Parsers;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Strategy.Transcribe;
using Centurion.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

// 设置控制台输出
ConsoleServices.Output = new SpectreConsoleOutput();
ConsoleServices.Progress = new SpectreProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

const string version = "alpha";
AnsiConsole.Write(new FigletText($"Centurion {version}"));

// 取消处理
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Cancellation requested...");
};

// ---------- DI 容器 ----------
var services = new ServiceCollection();
services.AddLogging();

// ---------- 基础设施 ----------
services.AddSingleton<IBinaryLocator, BinaryLocator>();
services.AddSingleton<CondaEnvironmentManager>();
services.AddSingleton<ITempDirectoryManager, TempDirectoryManager>();
services.AddSingleton<IModelPathResolver, ModelPathResolver>();

// ---------- 策略工厂（单例） ----------
services.AddSingleton<ITranscriptionStrategyFactory, TranscriptionStrategyFactory>();
services.AddSingleton<ISentenceSplitStrategyFactory, SentenceSplitStrategyFactory>();
services.AddSingleton<IAlignmentStrategyFactory, AlignmentStrategyFactory>();

// ---------- 转录策略（具体实现，供工厂使用） ----------
services.AddTransient<FasterWhisperStrategy>();

// ---------- 分句策略 ----------
services.AddTransient<RuleBasedSplitStrategy>();

// ---------- 对齐策略 ----------
services.AddTransient<NoOpAlignmentStrategy>();

// ---------- 管道算子（瞬态，每个管道执行新建） ----------
services.AddTransient<FFmpegConvertOperator>();
services.AddTransient<TranscribeOperator>();
services.AddTransient<CoarseSplitOperator>();      // 若仍有需要
services.AddTransient<DiarizationOperator>();      // 若仍有需要
services.AddTransient<SentenceSplitOperator>();
services.AddTransient<AlignmentOperator>();

// ---------- 其他辅助服务 ----------
services.AddSingleton<Centurion.Core.Operators.Downloader>();
services.AddSingleton<ISubtitleParser, SrtParser>();
services.AddSingleton<SubtitleConverter>();
services.AddSingleton<FFmpegManager>();

// ---------- 构建容器 ----------
var serviceProvider = services.BuildServiceProvider();

// ---------- 配置 Spectre.Cli 并注册命令 ----------
var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
    config.AddCommand<ConvertCommand>("convert");
});

// ---------- 运行 ----------
return await app.RunAsync(args);