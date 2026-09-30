using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Cli.Commands.Settings;
using Centurion.Models.Console;
using Centurion.Models.Metadata;
using Centurion.Core.Utils.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace Centurion.Cli.Commands;

/// <summary>Options for the doctor subcommand (inherits global --json).</summary>
public sealed class DoctorSettings : GlobalCommandSettings
{
}

/// <summary>
/// Environment and diagnostics probe: checks runtime, toolchain, models, config and
/// network, then writes a diagnostic log for issue reporting.
/// </summary>
public sealed class DoctorCommand(
    IDeviceDetector deviceDetector,
    IBinaryLocator binaryLocator,
    ModelRegistry registry,
    IServiceProvider serviceProvider,
    ILogger<DoctorCommand> logger) : AsyncCommand<DoctorSettings>
{
    private enum CheckStatus { Pass, Warn, Fail, Info }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken ct)
    {
        var checks = new List<DiagnosticCheck>();

        // 1. System / device
        try
        {
            var dev = deviceDetector.Detect();
            checks.Add(new("System", CheckStatus.Info, dev.DeviceSummary));
        }
        catch (Exception ex)
        {
            checks.Add(new("System", CheckStatus.Fail, ex.Message));
        }

        // 2. FFmpeg (PATH or bundled tools)
        var ffmpeg = ProbeBinary("ffmpeg", ["tools"], out var ffmpegPath);
        checks.Add(new("FFmpeg", ffmpeg ? CheckStatus.Pass : CheckStatus.Fail,
            ffmpeg ? $"{ffmpegPath ?? "?"} ({FfmpegVersion(ffmpegPath ?? "ffmpeg")})" : "not found on PATH or under tools/"));

        // 3. Bundled / third-party tools
        var tools = new (string Name, string Exe, string[] Dirs, string Note)[]
        {
            ("VideoSubFinder", "VideoSubFinderWXW.exe", ["tools/videosubfinder/Release_x64"], "auto-downloads on first OCR use"),
            ("RapidOCR", "rapidocr.exe", ["tools/rapidocr"], "auto-downloads models; CPU-only ONNX"),
            ("QORA-TTS", "qora-tts.exe", ["tools/qora-tts"], "auto-downloads 1.56GB weights on first dub use"),
            ("IndexTTS", "indextts.exe", ["tools/indextts"], "place manually (cargo build --release from 8b-is/IndexTTS-Rust)"),
            ("Llama.cpp", "llama-cli.exe", ["tools/llama"], "auto-downloads on first use"),
            ("Hunspell", "hunspell.exe", ["tools/hunspell"], "spellcheck dictionaries"),
            ("Encoderfile", "encoderfile.exe", ["tools/encoderfile"], "ASS encoding normalization")
        };
        foreach (var t in tools)
        {
            var found = ProbeBinary(t.Exe, t.Dirs, out var path);
            checks.Add(new($"Tool: {t.Name}",
                found ? CheckStatus.Pass : CheckStatus.Warn,
                found ? path ?? "?" : $"missing ({t.Note})"));
        }

        // QORA model weight (extra check: exe present but 1.56GB weight may be absent)
        var qoraExe = checks.FirstOrDefault(c => c.Name == "Tool: QORA-TTS");
        var qoraWeight = File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "qora-tts", "model.qora-tts"));
        if (qoraExe?.Status == CheckStatus.Pass)
            checks.Add(new("QORA-TTS weights", qoraWeight ? CheckStatus.Pass : CheckStatus.Warn,
                qoraWeight ? "model.qora-tts present" : "model.qora-tts missing (auto-download on first dub use)"));

        // 4. Model registry readiness
        var domains = ModelCatalog.Domains(registry);
        var ready = 0;
        var total = 0;
        foreach (var domain in domains)
        {
            foreach (var (name, _) in domain.Models)
            {
                total++;
                var mgr = ModelCatalog.CreateManager(serviceProvider, domain, name);
                if (ModelCatalog.ExistsLocally(mgr))
                    ready++;
            }
        }
        checks.Add(new("Models", ready == total ? CheckStatus.Pass : (ready == 0 ? CheckStatus.Warn : CheckStatus.Warn),
            $"{ready}/{total} ready (Centurion models install <model> to fetch missing ones)"));

        // 5. Configuration file
        var configPath = Path.Combine(AppContext.BaseDirectory, "centurion.config.json");
        if (!File.Exists(configPath))
            configPath = Path.Combine(Directory.GetCurrentDirectory(), "centurion.config.json");
        if (File.Exists(configPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                checks.Add(new("Config", CheckStatus.Pass, $"{configPath} (valid JSON)"));
            }
            catch (JsonException ex)
            {
                checks.Add(new("Config", CheckStatus.Fail, $"{configPath}: invalid JSON ({ex.Message})"));
            }
        }
        else
        {
            checks.Add(new("Config", CheckStatus.Info, "centurion.config.json not found (all defaults)"));
        }

        // 6. Network reachability (GitHub release API; model/tool downloads depend on it)
        var net = await ProbeGitHubAsync(ct);
        checks.Add(new("Network", net.ok ? CheckStatus.Pass : CheckStatus.Warn,
            net.ok ? "api.github.com reachable" : $"api.github.com unreachable ({net.error})"));

        // 7. Disk space for tools/models dir
        try
        {
            var toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools");
            var drive = Path.GetPathRoot(toolsRoot) ?? "C:\\";
            var free = new DriveInfo(drive).AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            checks.Add(new("Disk", free >= 3 ? CheckStatus.Pass : (free >= 1 ? CheckStatus.Warn : CheckStatus.Fail),
                $"{drive} {free:F1} GB free ({toolsRoot})"));
        }
        catch (Exception ex)
        {
            checks.Add(new("Disk", CheckStatus.Fail, ex.Message));
        }

        // Render
        if (settings.Json)
        {
            var json = JsonSerializer.Serialize(new
            {
                version = BuildInfo.DisplayVersion,
                generatedAt = DateTimeOffset.Now,
                checks = checks.Select(c => new { c.Name, status = c.Status.ToString().ToLowerInvariant(), c.Detail })
            }, new JsonSerializerOptions { WriteIndented = true });
            ConsoleServices.Output.WriteInfo(json);
        }
        else
        {
            var table = new Table()
                .Border(CliLayout.Border)
                .Title($"[bold]{ConsoleServices.T("Centurion Doctor")}[/]")
                .Width(CliLayout.TableWidth());
            table.AddColumn(new TableColumn(ConsoleServices.T("Check")).Width(24));
            table.AddColumn(new TableColumn(ConsoleServices.T("Status")).Width(10));
            table.AddColumn(new TableColumn(ConsoleServices.T("Detail")));
            foreach (var c in checks)
                table.AddRow(c.Name, StatusMarkup(c.Status), c.Detail);
            AnsiConsole.Write(table);

            var summary = checks.Count(c => c.Status == CheckStatus.Fail);
            var warnings = checks.Count(c => c.Status == CheckStatus.Warn);
            ConsoleServices.Output.WriteInfo(
                summary == 0
                    ? ConsoleServices.T("All checks passed ({0} warnings).", warnings)
                    : ConsoleServices.T("{0} failure(s), {1} warning(s) — see the diagnostic log for details.", summary, warnings));
        }

        // Diagnostic log for issue reporting
        var logPath = await WriteDiagnosticLogAsync(checks, ct);
        ConsoleServices.Output.WriteInfo(ConsoleServices.T("Diagnostic log written to {0}", logPath));

        return checks.Any(c => c.Status == CheckStatus.Fail) ? 1 : 0;
    }

    private bool ProbeBinary(string name, string[] localDirs, out string? path)
    {
        try
        {
            path = binaryLocator.Locate(name, localDirs);
            return true;
        }
        catch (BinaryNotFoundException)
        {
            path = null;
            return false;
        }
    }

    private static string FfmpegVersion(string ffmpegPath)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (proc is null)
                return "?";
            if (!proc.WaitForExit(3000))
            {
                proc.Kill();
                return "?";
            }
            var first = proc.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(first) ? "?" : first;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static async Task<(bool ok, string error)> ProbeGitHubAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://api.github.com");
            req.Headers.UserAgent.ParseAdd("Centurion-doctor");
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return ((int)resp.StatusCode < 500, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string StatusMarkup(CheckStatus s) => s switch
    {
        CheckStatus.Pass => $"[green]{ConsoleServices.T("PASS")}[/]",
        CheckStatus.Warn => $"[gold1]{ConsoleServices.T("WARN")}[/]",
        CheckStatus.Fail => $"[red]{ConsoleServices.T("FAIL")}[/]",
        _ => $"[blue]{ConsoleServices.T("INFO")}[/]"
    };

    private async Task<string> WriteDiagnosticLogAsync(IReadOnlyList<DiagnosticCheck> checks, CancellationToken ct)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(),
            $"centurion-doctor-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Centurion Doctor Report");
        sb.AppendLine();
        sb.AppendLine($"- Version: {BuildInfo.DisplayVersion}");
        sb.AppendLine($"- Generated: {DateTimeOffset.Now:O}");
        sb.AppendLine($"- OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- Process: {Environment.ProcessPath} ({Environment.ProcessId})");
        sb.AppendLine();
        sb.AppendLine("## Checks");
        sb.AppendLine();
        sb.AppendLine("| Check | Status | Detail |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var c in checks)
            sb.AppendLine($"| {c.Name} | {c.Status} | {c.Detail.Replace("|", "\\|")} |");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine();
        foreach (var kv in new Dictionary<string, string?>
        {
            ["CWD"] = Directory.GetCurrentDirectory(),
            ["BaseDir"] = AppContext.BaseDirectory,
            ["UserProfile"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["TERM"] = Environment.GetEnvironmentVariable("TERM"),
            ["WT_SESSION"] = Environment.GetEnvironmentVariable("WT_SESSION")
        })
            sb.AppendLine($"- {kv.Key}: {kv.Value}");

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        logger.LogInformation("Doctor report written to {Path}", path);
        return path;
    }

    private sealed record DiagnosticCheck(string Name, CheckStatus Status, string Detail);
}
