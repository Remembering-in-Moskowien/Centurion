using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { "python.exe", "python" }
            : new[] { "python3", "python" };

        foreach (var name in candidates)
        {
            // 传入 "python" 子目录作为自定义搜索目录，以支持嵌入式部署
            string? found = BinaryLocator.Locate(name, "python");
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

        // 写入输入
        await process.StandardInput.WriteAsync(jsonInput);
        process.StandardInput.Close();

        // 异步读取输出和错误
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new Exception($"Python script failed (code {process.ExitCode}): {error}");

        return output;
    }
}