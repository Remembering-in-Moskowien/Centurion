using System.Reflection;
using System.Text;
using System.Text.Json;
using Centurion.Abstractions.Commands;
using Centurion.Core.DependencyInjection;
using Centurion.Core.Infrastructure;
using Centurion.Server.Commands;
using Centurion.Server.Console;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 复用 Core 的依赖注入注册（策略工厂、管道算子、工具管理器、日志等）
builder.Services.AddCenturionCore();

var app = builder.Build();

// 命令内部的控制台输出 → Server 日志（避免 NullConsoleOutput 吞掉执行过程）
ConsoleServices.Output = new LoggerConsoleOutput(app.Logger);

var serviceProvider = app.Services;

// ---------- 端点 ----------

app.MapGet("/", () => Results.Ok(new
{
    name = "Centurion.Server",
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
    var assembly = typeof(Program).Assembly;
    var buildDate = File.GetLastWriteTime(assembly.Location);
    return Results.Ok(new
    {
        name = "Centurion.Server",
        version = assembly.GetName().Version?.ToString() ?? "0.0.0",
        buildDate = buildDate.ToString("O"),
        commands = ServerCommandRegistry.Names
    });
});

app.MapPost("/commands/{name}", async (
    string name,
    HttpRequest httpRequest,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger($"Centurion.Server.{name}");

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

    var settings = (CommandSettings?)Activator.CreateInstance(entry.Value.SettingsType);
    if (settings is null)
        return Results.Problem($"Failed to create settings for '{name}'.");

    ParameterBinder.Apply(commandRequest, settings, logger);

    var startedAt = DateTimeOffset.UtcNow;
    try
    {
        var command = ActivatorUtilities.CreateInstance(serviceProvider, entry.Value.CommandType);
        var executeMethod = entry.Value.CommandType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "ExecuteAsync" && m.GetParameters().Length == 3)
            ?? throw new InvalidOperationException($"Command '{name}' has no ExecuteAsync method.");

        var task = (Task)executeMethod.Invoke(command, [null, settings, ct])!;
        await task;
        var exitCode = ((Task<int>)task).Result;

        var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        logger.LogInformation("Command '{Command}' executed in {Duration} ms (exit {ExitCode})", name, durationMs, exitCode);

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
        logger.LogError(ex, "Command '{Command}' failed: {Message}", name, ex.Message);
        var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        return Results.Problem(
            ex is TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException.Message
                : ex.Message,
            statusCode: 500,
            title: "Command execution failed");
    }
});

app.Run();
