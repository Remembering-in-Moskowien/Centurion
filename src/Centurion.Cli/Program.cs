using Centurion.Cli;
using Centurion.Core.Utils.Infrastructure;
using Centurion.Models.Console;
using System.Globalization;
using Centurion.Cli.Commands;
using Centurion.Cli.Console;
using Centurion.Abstractions;
using Centurion.Core.Workflow.DependencyInjection;
using Centurion.Core.Capabilities.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using Spectre.Console.Cli;

// ----- Console setup (Output is wired to ILogger after the service provider is built) -----
ConsoleServices.Progress = new DotnetStyleProgressReporter();
ConsoleServices.Confirm = new SpectreConfirmPrompt();

// Use UTF-8 output on Windows consoles (legacy conhost needs SetConsoleOutputCP(65001) to display Chinese correctly).
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (Exception)
{
    // Keep the system default encoding if this fails; startup can continue.
}

// Detect terminal symbol support: use Unicode decorations on modern terminals and ASCII on legacy conhost.
Centurion.Models.Console.CliSymbols.Initialize(CliLayout.UnicodeSafe);

// Capture the OS UI language before setting the process defaults to invariant culture.
var systemUiLang = CultureInfo.CurrentUICulture.Name;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;



// --verbose / -v shows execution details (step timings and stage logs); by default, show only warnings and failures.
var verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)
    || a.Equals("-v", StringComparison.OrdinalIgnoreCase));
// --lang <CODE> selects the runtime UI language and its messages in Localization/{code}.json.
var lang = args.Where((a, i) => i > 0 && args[i - 1].Equals("--lang", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();
// --github-proxy <URL> / --no-github-proxy configures GitHub download mirrors; failed mirrors fall back to direct access.
var githubProxyArg = args.Where((a, i) => i > 0 && args[i - 1].Equals("--github-proxy", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();

// centurion.config.json and CENTURION_* environment variables provide defaults; command-line values take precedence.
var appConfig = CenturionConfig.Load();
var proxyValue = githubProxyArg ?? appConfig.GithubProxy;
if (args.Any(a => a.Equals("--no-github-proxy", StringComparison.OrdinalIgnoreCase)))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.Disabled = true;
else if (proxyValue == "")
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.Disabled = true;
else if (!string.IsNullOrWhiteSpace(proxyValue))
    Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.ProxyPrefix = proxyValue.EndsWith('/') ? proxyValue : proxyValue + "/";
// --profile <offline|fast|quality|cheap> selects provider preferences and cost priorities.
var profileArg = args.Where((a, i) => i > 0 && args[i - 1].Equals("--profile", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.TrimStart('-'))
    .FirstOrDefault();

var profileName = profileArg ?? appConfig.Profile;
if (!string.IsNullOrWhiteSpace(profileName))
    Centurion.Core.Providers.ProviderProfileResolver.Current =
        Centurion.Core.Providers.ProviderProfileResolver.FromString(profileName);

// --json suppresses human-readable output when enabled in config or on the command line.
var globalJson = appConfig.Json == true || args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
// --agent switches to LLM-friendly plain-text output (fixed INFO/OK/WARN/ERROR prefixes, no ANSI/timestamps/spinners/prompts).
var agentMode = args.Any(a => a.Equals("--agent", StringComparison.OrdinalIgnoreCase));

// ----- Brand banner (skipped in --json / --agent mode to keep stdout clean) -----
if (!globalJson && !agentMode)
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
        // Use build number 0 if the file cannot be read; startup can continue.
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
        && !a.Equals("--no-github-proxy", StringComparison.OrdinalIgnoreCase)
        && !(i > 0 && args[i - 1].Equals("--profile", StringComparison.OrdinalIgnoreCase))
        && !a.Equals("--profile", StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (lang is null && !string.IsNullOrWhiteSpace(appConfig.Language))
    lang = appConfig.Language;
// Command help and descriptions default to English; the language can be selected with --lang or centurion.config.json.

string? languageWarning = null;
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
        languageWarning = $"Unknown language code '{lang}'; falling back to English.";
    }
}

// ----- DI Container -----
var services = new ServiceCollection();
services.AddLogging(logging =>
{
    logging.SetMinimumLevel(globalJson || (!verbose && !agentMode) ? LogLevel.Warning : LogLevel.Information);
    // The file logger records every level, including info; console logging follows the global level.
    logging.AddFilter<Centurion.Core.Capabilities.Logging.FileLoggerProvider>(level => level >= LogLevel.Trace);
    // SpectreConsoleOutput renders info, error, and warning messages directly with custom colors.
    // Filter those console log events to prevent duplicates; Critical remains as a formatter fallback.
    logging.AddFilter<ConsoleLoggerProvider>("Centurion.Cli.Console.SpectreConsoleOutput", level => level >= LogLevel.Critical);
    // Console and file logs share the HH:mm:ss level: message format; colors depend on the log level.
    logging.AddConsole(options =>
    {
        options.FormatterName = "plain";
    });
    logging.AddConsoleFormatter<Centurion.Cli.Console.PlainConsoleFormatter, ConsoleFormatterOptions>();
    logging.AddProvider(new Centurion.Core.Capabilities.Logging.FileLoggerProvider());
});

// Register core services here (infrastructure, strategy factories, pipeline operators, and more).
services.AddCenturionCore();

// ----- Build service provider -----
var serviceProvider = services.BuildServiceProvider();

// Route console output through the logging system so it is also written to the logs directory.
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
ConsoleServices.Output = new SpectreConsoleOutput(loggerFactory.CreateLogger<SpectreConsoleOutput>());
if (globalJson)
    Centurion.Cli.Console.SpectreConsoleOutput.SuppressHumanLines = true;
Centurion.Cli.Console.SpectreConsoleOutput.AgentMode = agentMode;
if (agentMode)
{
    // Force Spectre rendering (tables, panels, etc.) to plain, non-interactive output
    // so captured output contains no ANSI escapes and prompts never block.
    AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
    AnsiConsole.Profile.Capabilities.Interactive = false;
}
if (languageWarning is not null)
    ConsoleServices.Output.WriteWarning(languageWarning);

// Cancellation token and handler are initialized after the custom output is available.
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    ConsoleServices.Output.WriteWarning(ConsoleServices.T("Cancellation requested..."));
};

// Load JSON localization messages for the selected language; English is used by default.
ConsoleServices.Localizer = new Centurion.Core.Capabilities.Localization.JsonStringLocalizerFactory().Create("Centurion");

// Root logger used by the top-level handlers; fatal and cancellation messages match the log file.
var rootLogger = loggerFactory.CreateLogger("Centurion.Cli.Program");
rootLogger.LogInformation("Centurion CLI started.");

// Detect the device and report GPU/memory details; compatible tool variants are downloaded when needed.
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
app.ConfigureCenturionCommands();

// ----- Run -----
try
{
    return await app.RunAsync(filteredArgs);
}
catch (OperationCanceledException)
{
    // Log through the shared channel so console warnings and file logs match.
    rootLogger.LogWarning(ConsoleServices.T("Operation cancelled by user."));
    return 130;
}
catch (Exception ex)
{
    // Handle uncaught exceptions with a clear error and exit code instead of an unhandled stack trace.
    // Log through the shared channel so console errors and file logs match.
    rootLogger.LogCritical("{Fatal}", ConsoleServices.T("Fatal: {0}", ex.Message));
    if (agentMode)
    {
        ConsoleServices.Output.WriteError(
            $"{ex.GetType().Name}: {ex.Message} — {ConsoleServices.T("full stack trace in logs directory, or retry with --verbose")}");
    }
    else
    {
        AnsiConsole.Write(new Panel(
                new Markup($"[bold red]{Centurion.Models.Console.CliSymbols.Cross} {ex.Message.EscapeMarkup()}[/]\n[dim]{ex.GetType().Name} — {ConsoleServices.T("full stack trace in logs directory, or retry with --verbose")}[/]"))
            .Header(ConsoleServices.T("Error"), Justify.Center)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red));
    }
    return 1;
}
