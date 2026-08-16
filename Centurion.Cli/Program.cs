using System.Globalization;
using System.Reflection;
using Centurion.Cli.Console;
using Centurion.Cli.Commands;
using Centurion.Core;
using Centurion.Core.Operators;
using Centurion.Core.Services;
using Centurion.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

// ---------- 配置 ConsoleServices ----------
ConsoleServices.Output = new SpectreConsoleOutput();
ConsoleServices.Progress = new SpectreProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

// ---------- 设置默认语言（可根据需要改为 zh） ----------
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en");

// ---------- 重写本地化文件目录 ----------
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    var name = new AssemblyName(args.Name);
    // 只处理资源程序集
    if (name.Name?.EndsWith(".resources") == true)
    {
        var culture = name.CultureInfo?.Name ?? "";
        // 构建新路径：Resources/{culture}/{AssemblyName}.resources.dll
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", culture, $"{name.Name}.dll");
        if (File.Exists(path))
            return Assembly.LoadFrom(path);
    }

    return null;
};

// ---------- 程序入口 ----------
const string version = "alpha";
AnsiConsole.Write(new FigletText($"Centurion {version}"));

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("Cancellation requested...");
};

// ---------- 构建 DI 容器 ----------
var services = new ServiceCollection();

// 注册本地化和日志服务
services.AddLocalization();
services.AddLogging();

// 注册核心服务
services.AddSingleton<IPythonInterop, PythonInteropService>();
services.AddSingleton<IBinaryLocator, BinaryLocator>();
services.AddTransient<FFmpegOperator>();
services.AddSingleton<AssSubSpawner>();
services.AddSingleton<SatSplitService>();
services.AddScoped<MfaCliOperator>();

services.AddTransient<AriaOperator>();

// 注册 SpeakerDiarizationService（其构造函数使用 [FromKeyedServices]）
services.AddSingleton<SpeakerDiarizationService>();

// ---------- 构建 CLI 应用 ----------
var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("spawn");
});

return await app.RunAsync(args);