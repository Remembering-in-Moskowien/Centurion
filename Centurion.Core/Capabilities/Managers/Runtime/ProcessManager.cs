using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Capabilities.Managers.Runtime;

/// <summary>
/// 进程执行器，返回原始标准输出，由调用方解析。
/// 支持超时、取消，并在进程退出前强制终止。
/// </summary>
public class ProcessManager(ILogger<ProcessManager> logger)
{
    /// <summary>
    /// 执行外部程序，返回标准输出字符串。
    /// </summary>
    /// <param name="executablePath">可执行文件完整路径</param>
    /// <param name="arguments">命令行参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>进程的标准输出内容</returns>
    /// <exception cref="TimeoutException">超时</exception>
    /// <exception cref="InvalidOperationException">进程退出码非0或无法启动</exception>
    public async Task<string> ExecuteAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken = default,
        bool throwOnNonZeroExit = true)
        => await ExecuteCoreAsync(executablePath, arguments, null, cancellationToken, throwOnNonZeroExit);

    /// <summary>
    /// 执行外部程序（参数数组形式），返回标准输出字符串。
    /// 使用 <see cref="ProcessStartInfo.ArgumentList"/> 传递参数，由系统负责正确转义，
    /// 调用方无需手工加引号，也避免路径/提示词中的引号破坏参数边界。
    /// </summary>
    /// <param name="executablePath">可执行文件完整路径</param>
    /// <param name="arguments">按顺序排列的参数列表（不含引号）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>进程的标准输出内容</returns>
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

        // 注册取消回调，强制终止进程
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
                    await process.WaitForExitAsync(cancellationToken); // 确保进程完全退出
                }
                throw new TimeoutException($"Process '{executablePath}' timed out.");
            }
        }

        if (process.ExitCode == 0) return outputBuilder.ToString();
        var error = errorBuilder.ToString();
        if (!throwOnNonZeroExit)
            return outputBuilder.ToString();
        // 进程失败属内部细节：以 warn 记录（异常继续上抛，由最外层统一输出一次 fail）
        logger.LogWarning(
            "Process '{Exe}' exited with code {ExitCode}. Error: {Error}",
            executablePath, process.ExitCode, error);
        throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}. Details: {error}");
    }
}