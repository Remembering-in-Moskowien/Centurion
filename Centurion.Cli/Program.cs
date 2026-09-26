using Centurion.Core.Utils.Infrastructure;
using Centurion.Models.Console;
using System.Globalization;
using Centurion.Cli.Commands;
using Centurion.Cli.Console;
using Centurion.Abstractions;
using Centurion.Core.Workflow.DependencyInjection;using Centurion.Core.Capabilities.Infrastructure;using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using Spectre.Console.Cli;

// ----- Console setup（Output 在 serviceProvider 构建后注入 ILogger 设置）-----
ConsoleServices.Progress = new DotnetStyleProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

AnsiConsole.Write(new FigletText("Centurion"));

// --verbose / -v：显示完整执行信息（步骤耗时、各阶段日志）；默认仅输出 warn/fail，控制台保持干净
var verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)
    || a.Equals("-v", StringComparison.OrdinalIgnoreCase));
// --lang <CODE>：运行时 UI 语言（如 zh-CN），决定 Localization/{code}.json 中的消息翻译
var lang = args.Where((a, i) => i > 0 && args[i - 1].Equals("--lang", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();
// --github-proxy <URL> / --no-github-proxy：GitHub 下载加速（默认启用 520 类镜像，失败自动回退直连）
var githubProxyArg = args.Where((a, i) => i > 0 && args[i - 1].Equals("--github-proxy", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();
if (args.Any(a => a.Equals("--no-github-proxy", StringComparison.OrdinalIgnoreCase)))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.Disabled = true;
else if (!string.IsNullOrWhiteSpace(githubProxyArg))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.ProxyPrefix = githubProxyArg.EndsWith('/') ? githubProxyArg : githubProxyArg + "/";
// --profile <offline|fast|quality|cheap>：Provider 选型 profile（本地/云互备策略、预算取向）
var profileArg = args.Where((a, i) => i > 0 && args[i - 1].Equals("--profile", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();
if (!string.IsNullOrWhiteSpace(profileArg))
    Centurion.Core.Providers.ProviderProfileResolver.Current =
        Centurion.Core.Providers.ProviderProfileResolver.FromString(profileArg);

var filteredArgs = args
    .Where((a, i) => !a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)
        && !a.Equals("-v", StringComparison.OrdinalIgnoreCase)
        && !(i > 0 && args[i - 1].Equals("--lang", StringComparison.OrdinalIgnoreCase))
        && !a.Equals("--lang", StringComparison.OrdinalIgnoreCase)
        && !(i > 0 && args[i - 1].Equals("--github-proxy", StringComparison.OrdinalIgnoreCase))
        && !a.Equals("--github-proxy", StringComparison.OrdinalIgnoreCase)
        && !a.Equals("--no-github-proxy", StringComparison.OrdinalIgnoreCase)        && !(i > 0 && args[i - 1].Equals("--profile", StringComparison.OrdinalIgnoreCase))
        && !a.Equals("--profile", StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (!string.IsNullOrWhiteSpace(lang))
{
    try
    {
        var culture = new CultureInfo(lang);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    catch (CultureNotFoundException)
    {
        ConsoleServices.Output.WriteWarning($"Unknown language code '{lang}'; falling back to English.");
    }
}

// Cancellation token
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    ConsoleServices.Output.WriteWarning(ConsoleServices.T("Cancellation requested..."));
};

// ----- DI Container -----
var services = new ServiceCollection();
services.AddLogging(logging =>
{
    logging.SetMinimumLevel(verbose ? LogLevel.Information : LogLevel.Warning);
    // 文件日志记录全部级别（含 info），控制台仍按全局级别过滤
    logging.AddFilter<Centurion.Core.Capabilities.Logging.FileLoggerProvider>(level => level >= LogLevel.Trace);
    // 控制台侧：SpectreConsoleOutput 的信息行已由渲染层直接输出，屏蔽其 info 避免重复显示
    logging.AddFilter<ConsoleLoggerProvider>("Centurion.Cli.Console.SpectreConsoleOutput", level => level >= LogLevel.Warning);
    // 控制台与文件日志使用同一格式（HH:mm:ss level: message）；颜色仅按级别渲染
    logging.AddConsole(options =>
    {
        options.FormatterName = "plain";
    });
    logging.AddConsoleFormatter<Centurion.Cli.Console.PlainConsoleFormatter, ConsoleFormatterOptions>();
    logging.AddProvider(new Centurion.Core.Capabilities.Logging.FileLoggerProvider());
});

// 核心服务注册集中于此（基础设施、策略工厂、管道算子等）
services.AddCenturionCore();

// ----- Build service provider -----
var serviceProvider = services.BuildServiceProvider();

// 控制台输出统一接入日志系统：每条控制台内容同时写入 ILogger（进而写入 logs 目录）
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
ConsoleServices.Output = new SpectreConsoleOutput(loggerFactory.CreateLogger<SpectreConsoleOutput>());

// JSON 本地化：消息按 --lang 选择的语言输出（Localization/{lang}.json，缺省英文）
ConsoleServices.Localizer = new Centurion.Core.Capabilities.Localization.JsonStringLocalizerFactory().Create("Centurion");

// 顶层 catch 使用的根日志器（Fatal/取消提示与日志文件逐字一致）
var rootLogger = loggerFactory.CreateLogger("Centurion.Cli.Program");
rootLogger.LogInformation("Centurion CLI started.");

// 设备检测：打印 GPU/内存摘要（GPU 可用时工具将自动下载对应变体，如 whisper.cpp CUDA 版）
try
{
    var detected = serviceProvider.GetRequiredService<IDeviceDetector>().Detect();
    ConsoleServices.Output.WriteInfo(ConsoleServices.T("Device: {0}", detected.DeviceSummary));
}
catch (Exception ex)
{
    ConsoleServices.Output.WriteInfo(ConsoleServices.T("Device detection failed: {0}", ex.Message));
}

// ----- Configure Spectre.Cli -----
var registrar = new Centurion.Cli.TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("Centurion");
    config.AddCommand<SpawnCommand>("asr");
    config.AddCommand<OcrCommand>("ocr");
    config.AddCommand<FromScriptCommand>("from-script");
    config.AddCommand<CorrectCommand>("correct");
    config.AddCommand<TranslateCommand>("translate");
    config.AddCommand<DubCommand>("dub");
    config.AddCommand<ConvertCommand>("convert");
    config.AddCommand<BuildCommand>("build");
    config.AddCommand<UpdateCommand>("update");
    // 单算子小命令已移除：全部核心命令统一走 DAG 管线（asr/ocr/from-script/translate/dub/correct/quality）
    // REST 服务：POST /commands/{name} 复用 CLI 命令内核（CommandRequest 契约）
    config.AddCommand<ValidateCommand>("validate");
    config.AddCommand<MigrateCommand>("migrate");
    config.AddCommand<QualityCommand>("quality");
    // 管线 DAG 可视化（不执行，只渲染拓扑）
    config.AddCommand<PipelineGraphCommand>("pipeline-graph");
    // 模型注册表管理 + Provider 选型/探测
    config.AddBranch("models", models =>
    {
        models.SetDescription("Model registry management (list/install/verify/remove).");
        models.AddCommand<ModelsListCommand>("list");
        models.AddCommand<ModelsInstallCommand>("install");
        models.AddCommand<ModelsVerifyCommand>("verify");
        models.AddCommand<ModelsRemoveCommand>("remove");
    });
    config.AddBranch("providers", providers =>
    {
        providers.SetDescription("Provider inspection (list/test).");
        providers.AddCommand<ProvidersListCommand>("list");
        providers.AddCommand<ProvidersTestCommand>("test");
    });
});

// ----- Run -----
try
{
    return await app.RunAsync(filteredArgs);
}
catch (OperationCanceledException)
{
    // 经日志通道输出，控制台（黄）与日志文件（warn）逐字一致
    rootLogger.LogWarning(ConsoleServices.T("Operation cancelled by user."));
    return 130;
}
catch (Exception ex)
{
    // 顶层兜底：任何未捕获异常都以明确的错误与退出码结束，避免裸栈崩溃；
    // 经日志通道输出，控制台（红）与日志文件（crit）逐字一致
    rootLogger.LogCritical("{Fatal}", ConsoleServices.T("Fatal: {0}", ex.Message));
    return 1;
}
