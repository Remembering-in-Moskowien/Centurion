using Centurion.Abstractions;
using Centurion.Abstractions.Commands;
using Centurion.Cli.Commands.Settings;
using Centurion.Cli.Server;
using Centurion.Models.Console;
using Centurion.Core.Workflow.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;
using System.Text.Json;

namespace Centurion.Cli.Commands;

/// <summary>Options for the serve subcommand: an HTTP service reusing all DAG commands (POST /commands/{name}).</summary>
public sealed class ServeSettings : GlobalCommandSettings
{
    /// <summary>Listen host (default localhost).</summary>
    [CommandOption("--host <HOST>")]
    public string Host { get; init; } = "localhost";

    /// <summary>Listen port (default 8080).</summary>
    [CommandOption("-p|--port <PORT>")]
    public int Port { get; init; } = 8080;

    /// <summary>Full listen URL list (overrides --host/--port).</summary>
    [CommandOption("--urls <URLS>")]
    public string? Urls { get; init; }
}

/// <summary>
/// serve: exposes all packaged commands as an HTTP service
/// (asr/from-script/correct/translate/dub/convert/build). Reuses the Cli command
/// registry and Core DI; the request body is a CommandRequest JSON or a bare parameter object.
/// </summary>
public sealed class ServeCommand(
    ILogger<ServeCommand> logger) : AsyncCommand<ServeSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ServeSettings settings, CancellationToken ct)
    {
        var urls = settings.Urls ?? $"http://{settings.Host}:{settings.Port}";

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine($"[bold cyan]{CliSymbols.Play} {ConsoleServices.T("serve")}[/] {ConsoleServices.T("listening on")} [bold]{urls}[/]");
            AnsiConsole.MarkupLine($"{ConsoleServices.T("Endpoints:")}");
            foreach (var e in ServerCommandRegistry.Names)
                AnsiConsole.MarkupLine($"  [dim]POST /commands/{e}[/]");
            AnsiConsole.MarkupLine($"[dim]GET /  /health  /commands  /version[/]");
            return ExitCodes.Success;
        }

        var builder = WebApplication.CreateBuilder();
        // Suppress ASP.NET Core startup messages such as Kestrel's "Now listening on" lines.
        // The serve command prints its own startup banner; command logs keep the main CLI's plain formatter and file logger.
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
        // Suppress request-pipeline noise such as "Request starting" and "Executing endpoint".
        // Keep command execution logs under Centurion.Server.*.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
        builder.Logging.AddConsole(options => options.FormatterName = "plain");
        builder.Logging.AddConsoleFormatter<Centurion.Cli.Console.PlainConsoleFormatter, ConsoleFormatterOptions>();
        builder.Logging.AddProvider(new Centurion.Core.Capabilities.Logging.FileLoggerProvider());
        builder.Services.AddCenturionCore();

        builder.WebHost.UseUrls(urls);

        var app = builder.Build();
        // Route command output to server logs so NullConsoleOutput does not discard execution details.
        ConsoleServices.Output = new LoggerConsoleOutput(app.Logger);
        var serviceProvider = app.Services;

        // ---------- Endpoints ----------
        app.MapGet("/", () => Results.Ok(new
        {
            name = "Centurion",
            commands = ServerCommandRegistry.Names.Length,
            health = "/health",
            commandsEndpoint = "/commands",
            execute = "POST /commands/{name}"
        }));

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            commands = ServerCommandRegistry.Names.Length
        }));

        app.MapGet("/commands", () => Results.Ok(ServerCommandRegistry.Names));

        app.MapGet("/version", () =>
        {
            var buildNumber = "0";
            try
            {
                var buildFile = Path.Combine(AppContext.BaseDirectory, "build-number.txt");
                if (File.Exists(buildFile))
                    buildNumber = File.ReadAllText(buildFile).Trim();
            }
            catch (Exception)
            {
                // Ignore read failures.
            }
            return Results.Ok(new
            {
                name = "Centurion",
                build = buildNumber,
                commands = ServerCommandRegistry.Names
            });
        });

        app.MapPost("/commands/{name}", async (
            string name,
            HttpRequest httpRequest,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var commandLogger = loggerFactory.CreateLogger($"Centurion.Server.{name}");

            var entry = ServerCommandRegistry.Resolve(name);
            if (entry is null)
                return Results.NotFound(new
                {
                    error = $"Unknown command '{name}'. Available: {string.Join(", ", ServerCommandRegistry.Names)}"
                });

            var body = await new StreamReader(httpRequest.Body, Encoding.UTF8).ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Request body is required (CommandRequest JSON or a flat parameters object)." });

            CommandRequest commandRequest;
            try
            {
                commandRequest = ParameterBinder.ParseBody(body, name);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid JSON body: {ex.Message}" });
            }

            var settings2 = (CommandSettings?)Activator.CreateInstance(entry.Value.SettingsType);
            if (settings2 is null)
                return Results.Problem($"Failed to create settings for '{name}'.");

            ParameterBinder.Apply(commandRequest, settings2, commandLogger);

            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                var command = ActivatorUtilities.CreateInstance(serviceProvider, entry.Value.CommandType);
                var executeMethod = entry.Value.CommandType
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "ExecuteAsync" && m.GetParameters().Length == 3)
                    ?? throw new InvalidOperationException($"Command '{name}' has no ExecuteAsync method.");

                var task = (Task)executeMethod.Invoke(command, [null, settings2, ct])!;
                await task;
                var exitCode = ((Task<int>)task).Result;

                var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                commandLogger.LogInformation("Command '{Command}' executed in {Duration} ms (exit {ExitCode})", name, durationMs, exitCode);

                return Results.Ok(new
                {
                    command = name,
                    exitCode,
                    durationMs,
                    startedAt = startedAt.ToString("O"),
                    finishedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }
            catch (Exception ex)
            {
                commandLogger.LogError(ex, "Command '{Command}' failed: {Message}", name, ex.Message);
                var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                return Results.Problem(
                    ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                        ? tie.InnerException.Message
                        : ex.Message,
                    statusCode: 500,
                    title: "Command execution failed");
            }
        });

        // Print the serve startup banner in place of the suppressed ASP.NET Core messages.
        AnsiConsole.MarkupLine($"[bold cyan]{CliSymbols.Play} {ConsoleServices.T("serve")}[/] {ConsoleServices.T("listening on")} [bold]{urls}[/]");
        AnsiConsole.MarkupLine($"{ConsoleServices.T("Endpoints:")}");
        foreach (var e in ServerCommandRegistry.Names)
            AnsiConsole.MarkupLine($"  [dim]POST /commands/{e}[/]");
        AnsiConsole.MarkupLine($"[dim]GET /  /health  /commands  /version[/]");
        AnsiConsole.MarkupLine($"[dim]{ConsoleServices.T("Press Ctrl+C to shut down")}[/]");
        logger.LogInformation("Centurion serve listening on {Urls}", urls);

        // WebApplication handles Ctrl+C/SIGTERM itself. Program's CancelKeyPress handler sets e.Cancel,
        // which does not interfere with ASP.NET shutdown; returning from RunAsync means the server has stopped.
        await app.RunAsync();
        AnsiConsole.MarkupLine($"[dim]{ConsoleServices.T("serve stopped")}[/]");
        return ExitCodes.Success;
    }
}
