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
    var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "dev";
    AnsiConsole.Write(new Markup($"[dim]v{version} · 字幕工作流 CLI[/]").Centered());
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
        .WithDescription("交互式向导：生成 centurion.config.json 与推荐命令链（转写→翻译→出字幕）");
    // ─── 核心字幕管线（统一走 DAG，pipeline-graph 可查看拓扑） ───
    config.AddCommand<SpawnCommand>("asr")
        .WithDescription("自动字幕生成：音视频 → 转录/说话人分割/分句/对齐 → Centurion 中间文件");
    config.AddCommand<OcrCommand>("ocr")
        .WithDescription("视频/图片字幕识别：抽帧 OCR（RapidOCR/LLM）→ 分句/清洗 → 中间文件");
    config.AddCommand<FromScriptCommand>("from-script")
        .WithDescription("脚本对齐：按台本对媒体转录并映射时间轴 → 中间文件");
    config.AddCommand<CorrectCommand>("correct")
        .WithDescription("字幕校正：按参考脚本/音频时间轴校正文本与时间线（timeline-only/text-only/both）");
    config.AddCommand<TranslateCommand>("translate")
        .WithDescription("字幕翻译：LLM 策略 + 术语表/目标台本 1:1 对齐，支持双语输出");
    config.AddCommand<DubCommand>("dub")
        .WithDescription("媒体译制：说话人画像 → TTS 合成 → 时间对齐 → 混音 → 译制 wav");
    config.AddCommand<ConvertCommand>("convert")
        .WithDescription("字幕转换：解析 ASS/SRT/TXT 字幕 → Centurion 中间文件");
    config.AddCommand<BuildCommand>("build")
        .WithDescription("字幕构建：中间文件 → ASS/SRT/TXT 字幕");
    // ─── 质量与工具 ───
    config.AddCommand<QualityCommand>("quality")
        .WithDescription("质量报告：输出 .quality.json/.html，--fix 自动修复，--fail-on CI 阈值门禁");
    config.AddCommand<PipelineGraphCommand>("pipeline-graph")
        .WithDescription("管线 DAG 可视化：渲染指定命令的拓扑（不执行，-c 选命令）");
    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("校验 Centurion 中间文件是否符合 IR Schema");
    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("IR Schema 迁移：把旧版本中间文件升级到指定版本（--to）");
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("自更新：从 GitHub Releases 检查/下载/应用新版本");
    // ─── 模型注册表管理 + Provider 选型/探测 ───
    config.AddBranch("models", models =>
    {
        models.SetDescription("模型注册表管理：列出/安装/校验/移除本地模型（whisper/qwen3/diarization/bert/tts）");
        models.AddCommand<ModelsListCommand>("list")
            .WithDescription("列出全部注册模型与本地就绪状态（表格 + 徽章）");
        models.AddCommand<ModelsInstallCommand>("install")
            .WithDescription("下载安装指定模型（缺失时运行会提示此命令）");
        models.AddCommand<ModelsVerifyCommand>("verify")
            .WithDescription("校验模型本地文件是否就绪（缺失返回退出码 1）");
        models.AddCommand<ModelsRemoveCommand>("remove")
            .WithDescription("删除模型本地文件/目录（需确认）");
    });
    config.AddBranch("providers", providers =>
    {
        providers.SetDescription("Provider 检查：列出/探测本地与云推理提供方（成本/延迟/质量）");
        providers.AddCommand<ProvidersListCommand>("list")
            .WithDescription("列出全部 Provider 及其能力与可用性（表格 + 成本图表）");
        providers.AddCommand<ProvidersTestCommand>("test")
            .WithDescription("探测指定 Provider 的可用性并显示能力声明");
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
            new Markup($"[bold red]✖ {ex.Message.EscapeMarkup()}[/]\n[dim]{ex.GetType().Name} — 完整堆栈见 logs 目录，或加 --verbose 重试[/]"))
        .Header("错误", Justify.Center)
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Red));
    return 1;
}
