using System.Globalization;
using Centurion.Cli.Console;
using Centurion.Cli.Commands;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Operators;
using Centurion.Core.Parsers;
using Centurion.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

ConsoleServices.Output = new SpectreConsoleOutput();
ConsoleServices.Progress = new SpectreProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en");

const string version = "alpha";
AnsiConsole.Write(new FigletText($"Centurion {version}"));

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Cancellation requested...");
};

var services = new ServiceCollection();
services.AddLogging();

// 基础服务
services.AddSingleton<IBinaryLocator, BinaryLocator>();
services.AddSingleton<CondaEnvironmentManager>();

// ---------- 算子注册 ----------
// Aria 下载
services.AddSingleton<AriaOperator>();

// FFmpeg（拆分）
services.AddTransient<FFmpegConvertOperator>();
services.AddTransient<FFmpegSplitOperator>();

// Catalyst 分句
var modelCachePath = Path.Combine(AppContext.BaseDirectory, "models", "catalyst");
var catalystOptions = new CatalystSplitOptions
{
    ModelCachePath = modelCachePath,
    DefaultLanguage = "en",
    DefaultMaxLength = 80,
    DefaultTargetLength = 50
};
services.AddSingleton<CatalystSplitOperator>(sp => new CatalystSplitOperator(catalystOptions));

// 说话人分割
services.Configure<DiarizationOptions>(options =>
{
    options.ModelName = "voxceleb_resnet293_LM";
    options.ClusterThreshold = 0.55;
});
services.AddSingleton<DiarizationOperator>();

// MFA 对齐
services.AddSingleton<SpeakerMfaAlignmentOperator>();

// 字幕转换（SRT → ASS）
services.AddSingleton<ISubtitleParser, SrtParser>();
services.AddSingleton<SubtitleConverterOperator>();

// 字幕生成（媒体）
services.AddSingleton<SubtitleGeneratorOperator>();

// 注册临时目录管理器（单例）
services.AddSingleton<ITempDirectoryManager, TempDirectoryManager>();

var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
    config.AddCommand<ConvertCommand>("convert");
});

return await app.RunAsync(args);