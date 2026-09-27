using Centurion.Cli;
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

// Windows 控制台统一 UTF-8 输出（旧 conhost 需 SetConsoleOutputCP(65001) 才能正确显示中文）
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (Exception)
{
    // 编码设置失败时保持系统默认，不影响后续逻辑
}

// 终端符号能力：现代终端用 Unicode 装饰，旧 conhost 降级 ASCII（避免不可识别符号）
Centurion.Models.Console.CliSymbols.Initialize(CliLayout.UnicodeSafe);

// 系统 UI 语言快照：进程启动时 .NET 已按 OS 首选项初始化，先保存再统一强制 Invariant
var systemUiLang = CultureInfo.CurrentUICulture.Name;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;



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

// centurion.config.json + CENTURION_* 环境变量：作为默认值（命令行参数优先）
var appConfig = CenturionConfig.Load();
var proxyValue = githubProxyArg ?? appConfig.GithubProxy;
if (args.Any(a => a.Equals("--no-github-proxy", StringComparison.OrdinalIgnoreCase)))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.Disabled = true;
else if (proxyValue == "")
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.Disabled = true;
else if (!string.IsNullOrWhiteSpace(proxyValue))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.ProxyPrefix = proxyValue.EndsWith('/') ? proxyValue : proxyValue + "/";
// --profile <offline|fast|quality|cheap>：Provider 选型 profile（本地/云互备策略、预算取向）
var profileArg = args.Where((a, i) => i > 0 && args[i - 1].Equals("--profile", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();

var profileName = profileArg ?? appConfig.Profile;
if (!string.IsNullOrWhiteSpace(profileName))
    Centurion.Core.Providers.ProviderProfileResolver.Current =
        Centurion.Core.Providers.ProviderProfileResolver.FromString(profileName);

// --json 全局开关：config.Json 或命令行 --json 任一开启 → 抑制人类可读行，仅输出 JSON
var globalJson = appConfig.Json == true || args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));

// ----- Brand banner（--json 模式跳过，保持 stdout 纯净）-----
if (!globalJson)
{
    AnsiConsole.Write(new FigletText("Centurion").Centered().Color(Color.Aqua));
    var buildNumber = "0";
    try
    {
        var buildFile = Path.Combine(AppContext.BaseDirectory, "build-number.txt");
        if (File.Exists(buildFile))
            buildNumber = File.ReadAllText(buildFile).Trim();
    }
    catch (Exception)
    {
        // 读取失败时按 0 处理，不影响启动
    }
    AnsiConsole.Write(new Markup($"[dim]Build #{buildNumber} · {ConsoleServices.T("Subtitle Workflow CLI")}[/]").Centered());
    AnsiConsole.Write(new Rule().RuleStyle("grey"));
}

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

if (lang is null && !string.IsNullOrWhiteSpace(appConfig.Language))
    lang = appConfig.Language;
// 自动检测环境语言首选项：系统 UI 为中文时默认中文，其余语言默认英文（英文优先）
if (lang is null && systemUiLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
    lang = "zh-CN";

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
    logging.SetMinimumLevel(globalJson || !verbose ? LogLevel.Warning : LogLevel.Information);
    // 文件日志记录全部级别（含 info），控制台仍按全局级别过滤
    logging.AddFilter<Centurion.Core.Capabilities.Logging.FileLoggerProvider>(level => level >= LogLevel.Trace);
    // 控制台侧：SpectreConsoleOutput 的 info/error/warning 均由渲染层直接输出（自定义颜色），
    // 屏蔽其日志通道避免重复显示；Critical 仍由日志格式化器渲染兜底
    logging.AddFilter<ConsoleLoggerProvider>("Centurion.Cli.Console.SpectreConsoleOutput", level => level >= LogLevel.Critical);
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
if (globalJson)
    Centurion.Cli.Console.SpectreConsoleOutput.SuppressHumanLines = true;

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
    // ─── 新手指引 ───
    config.AddCommand<InitCommand>("init")
        .WithDescription(ConsoleServices.T("Interactive wizard: generates centurion.config.json and a recommended command chain (transcribe → translate → subtitles)"));
    // ─── 核心字幕管线（统一走 DAG，pipeline-graph 可查看拓扑） ───
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
    config.AddCommand<ServeCommand>("serve")
        .WithDescription(ConsoleServices.T("HTTP service: expose packaged commands via POST /commands/{name} (REST)"));
    config.AddCommand<BuildCommand>("build")
        .WithDescription(ConsoleServices.T("Subtitle build: intermediate file → ASS/SRT/TXT subtitles"));
    // ─── 质量与工具 ───
    config.AddCommand<QualityCommand>("quality")
        .WithDescription(ConsoleServices.T("Quality report: .quality.json/.html, --fix auto-repair, --fail-on CI thresholds"));
    config.AddCommand<PipelineGraphCommand>("pipeline-graph")
        .WithDescription(ConsoleServices.T("Pipeline DAG visualization: render a command's topology (no execution, -c selects)"));
    config.AddCommand<ValidateCommand>("validate")
        .WithDescription(ConsoleServices.T("Validate Centurion intermediate files against the IR schema"));
    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription(ConsoleServices.T("IR schema migration: upgrade older intermediate files to a target version (--to)"));
    config.AddCommand<UpdateCommand>("update")
        .WithDescription(ConsoleServices.T("Self-update: check/download/apply new releases from GitHub Releases"));
    // ─── 模型注册表管理 + Provider 选型/探测 ───
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
    AnsiConsole.Write(new Panel(
            new Markup($"[bold red]{Centurion.Models.Console.CliSymbols.Cross} {ex.Message.EscapeMarkup()}[/]\n[dim]{ex.GetType().Name} — {ConsoleServices.T("full stack trace in logs directory, or retry with --verbose")}[/]"))
        .Header(ConsoleServices.T("Error"), Justify.Center)
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Red));
    return 1;
}
