// File: Centurion.Core/Managers/ProcessManager.cs
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Managers;

/// <summary>
/// 进程执行器，返回原始标准输出，由调用方解析。
/// 支持超时、取消，并在进程退出前强制终止。
/// </summary>
public class ProcessManager(ILogger logger)
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
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException($"Executable not found: {executablePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

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
        logger.LogError("Process '{Exe}' exited with code {ExitCode}. Error: {Error}",
            executablePath, process.ExitCode, error);
        throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}. Details: {error}");

    }
}