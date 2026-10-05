using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Capabilities.Managers.Runtime;

/// <summary>
/// Process executor that returns raw standard output for the caller to parse.
/// Supports timeouts and cancellation, and force-terminates the process on exit.
/// </summary>
public class ProcessManager(ILogger<ProcessManager> logger)
{
    /// <summary>
    /// Runs an external program and returns its standard output as a string.
    /// </summary>
    /// <param name="executablePath">Full path to the executable</param>
    /// <param name="arguments">Command-line arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="throwOnNonZeroExit">Whether to throw InvalidOperationException when the process exit code is non-zero (default true)</param>
    /// <returns>The process's standard output</returns>
    /// <exception cref="TimeoutException">Timed out</exception>
    /// <exception cref="InvalidOperationException">The process exited with a non-zero code or could not be started</exception>
    public async Task<string> ExecuteAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken = default,
        bool throwOnNonZeroExit = true)
        => await ExecuteCoreAsync(executablePath, arguments, null, cancellationToken, throwOnNonZeroExit);

    /// <summary>
    /// Runs an external program (argument-array form) and returns its standard output as a string.
    /// Uses <see cref="ProcessStartInfo.ArgumentList"/> to pass arguments, letting the system handle
    /// escaping correctly; the caller need not add quotes manually, and quotes inside paths/prompts
    /// cannot break the argument boundaries.
    /// </summary>
    /// <param name="executablePath">Full path to the executable</param>
    /// <param name="arguments">Ordered argument list (without quotes)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="throwOnNonZeroExit">Whether to throw InvalidOperationException when the process exit code is non-zero (default true)</param>
    /// <returns>The process's standard output</returns>
    public async Task<string> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        bool throwOnNonZeroExit = true)
        => await ExecuteCoreAsync(executablePath, null, arguments, cancellationToken, throwOnNonZeroExit);

    private async Task<string> ExecuteCoreAsync(
        string executablePath,
        string? arguments,
        IReadOnlyList<string>? argumentList,
        CancellationToken cancellationToken,
        bool throwOnNonZeroExit = true)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException($"Executable not found: {executablePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (argumentList is not null)
        {
            foreach (var arg in argumentList)
                startInfo.ArgumentList.Add(arg);
        }
        else
        {
            startInfo.Arguments = arguments!;
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Register a cancellation callback that force-terminates the process
        await using (cts.Token.Register(() =>
                     {
                         if (process.HasExited) return;
                         try { process.Kill(); }
                         catch (Exception ex) { logger.LogWarning(ex, "Failed to kill process during cancellation."); }
                     }))
        {
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync(cancellationToken); // Ensure the process exits completely
                }
                throw new TimeoutException($"Process '{executablePath}' timed out.");
            }
        }

        if (process.ExitCode == 0) return outputBuilder.ToString();
        var error = errorBuilder.ToString();
        if (!throwOnNonZeroExit)
            return outputBuilder.ToString();
        // A process failure is an internal detail: log at warn (the exception keeps propagating
        // and the outermost layer prints a single fail).
        logger.LogWarning(
            "Process '{Exe}' exited with code {ExitCode}. Error: {Error}",
            executablePath, process.ExitCode, error);
        throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}. Details: {error}");
    }
}