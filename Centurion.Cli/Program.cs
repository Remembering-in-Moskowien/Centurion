// File: Program.cs
using System.Globalization;
using Centurion.Cli.Commands;
using Centurion.Cli.Console;
using Centurion.Core.DependencyInjection;
using Centurion.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
services.AddLogging(logging => logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
}));

// 核心服务注册集中于此（基础设施、策略工厂、管道算子等）
services.AddCenturionCore();

// ----- Build service provider -----
_ = services.BuildServiceProvider();

// ----- Configure Spectre.Cli -----
var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
    config.AddCommand<FromScriptCommand>("from-script");
    config.AddCommand<CorrectCommand>("correct");
    config.AddCommand<ConvertCommand>("convert");
});

// ----- Run -----
return await app.RunAsync(args);
