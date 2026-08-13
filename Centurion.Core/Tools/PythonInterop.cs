using System.Diagnostics;
using System.Text;

namespace Centurion.Core.Tools;

/// <summary>
/// Python 交互工具：定位解释器、自动安装依赖、执行脚本
/// </summary>
public static class PythonInterop
{
    private static string? _pythonPath;

    /// <summary>
    /// 查找系统中的 Python 解释器（版本 >= 3.9）
    /// 优先通过 BinaryLocator 搜索，回退到常见安装路径
    /// </summary>
    public static async Task<string> LocatePythonAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_pythonPath) && File.Exists(_pythonPath))
            return _pythonPath;

        // 1. 通过 BinaryLocator 查找（先尝试 python，再 python3）
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python.exe", "python" }
            : new[] { "python3", "python" };

        foreach (var name in candidates)
        {
            // 传入 "python" 子目录作为自定义搜索目录，以支持嵌入式部署
            var found = BinaryLocator.Locate(name, "python");
            if (File.Exists(found))
            {
                _pythonPath = found;
                return found;
            }
        }

        throw new InvalidOperationException(
            "Python 3.9+ not found. Please install Python and ensure it is in PATH.");
    }

    /// <summary>
    /// 确保 wtpsplit 已安装，若未安装则自动联网安装
    /// </summary>
    public static async Task EnsureDependenciesAsync(string pythonPath, CancellationToken ct = default)
    {
        // 检查是否已安装
        var checkPsi = new ProcessStartInfo(pythonPath, "-m pip show wtpsplit[onnx-cpu]")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var check = Process.Start(checkPsi);
        if (check == null) throw new Exception("Failed to start pip check.");
        await check.WaitForExitAsync(ct);
        if (check.ExitCode == 0)
            return; // 已安装

        // 未安装，执行安装
        var installPsi = new ProcessStartInfo(pythonPath, "-m pip install wtpsplit[onnx-cpu]")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var install = Process.Start(installPsi);
        if (install == null) throw new Exception("Failed to start pip install.");
        var output = await install.StandardOutput.ReadToEndAsync();
        var error = await install.StandardError.ReadToEndAsync();
        await install.WaitForExitAsync(ct);
        if (install.ExitCode != 0)
            throw new Exception($"Failed to install wtpsplit: {error}");
    }

    /// <summary>
    /// 执行 Python 脚本，通过 stdin 传入 JSON，返回 stdout 的 JSON
    /// </summary>
    public static async Task<string> RunScriptAsync(
        string pythonPath,
        string scriptPath,
        string jsonInput,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(pythonPath, $"\"{scriptPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Failed to start Python process.");

        // 异步读取 stdout/stderr，防止死锁
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // 检查进程是否立即退出
        if (process.HasExited)
        {
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
               throw new Exception($"Python process exited immediately (exit code {process.ExitCode}). stderr: {stderr}");
        }

        // 尝试写入 stdin
        try
        {
            await process.StandardInput.WriteAsync(jsonInput);
            process.StandardInput.Close();
        }
        catch (IOException ex) when (ex.Message.Contains("pipe"))
        {
            // 管道错误，可能进程在写入时退出
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            throw new Exception($"Failed to write to stdin (pipe closed). Process may have exited. stderr: {stderr}", ex);
        }

        // 等待进程退出
        await process.WaitForExitAsync(ct);

        var stdoutFinal = await stdoutTask;
        var stderrFinal = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new Exception($"Python script exited with code {process.ExitCode}. stderr: {stderrFinal}");
        }

        return stdoutFinal;
    }
}