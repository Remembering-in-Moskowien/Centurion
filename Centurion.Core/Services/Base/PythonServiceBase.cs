using System.Diagnostics;
using System.Text;
using Centurion.Core.Models;

namespace Centurion.Core.Services.Base;

/// <summary>
/// Python 服务基类，负责进程生命周期、stdin/stdout 通信和 stderr 日志。
/// 模型管理由可选的 ModelManager 提供，通过构造函数注入。
/// </summary>
public abstract class PythonServiceBase : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // 可选的模型管理器（由子类决定是否注入）
    private readonly ModelManager? _modelManager;

    // 子类必须提供 Python 脚本文件名（嵌入资源的键）
    protected abstract string ScriptName { get; }

    // 子类可重写启动后的等待时间（模型加载耗时）
    protected virtual int StartupDelayMs => 5000;

    /// <summary>
    /// 构造器，允许注入可选的 ModelManager。
    /// </summary>
    protected PythonServiceBase(ModelManager? modelManager = null)
    {
        _modelManager = modelManager;
    }

    /// <summary>
    /// 确保模型可用（默认空实现，子类可重写以调用 _modelManager?.EnsureModelAvailableAsync()）
    /// </summary>
    public virtual async Task EnsureModelAvailableAsync()
    {
        if (_modelManager != null)
            await _modelManager.EnsureModelAvailableAsync();
        // 否则不做任何事
    }

    /// <summary>
    /// 启动 Python 服务进程，并准备通信管道。
    /// </summary>
    public async Task StartAsync(string pythonPath, CancellationToken ct = default)
    {
        // 确保模型已就绪（如果子类或注入的 ModelManager 需要）
        await EnsureModelAvailableAsync();

        if (_process != null && !_process.HasExited)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_process != null && !_process.HasExited)
                return;

            var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", ScriptName);
            if (!File.Exists(scriptPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
                var scriptContent = EmbeddedResources.GetScript(ScriptName);
                await File.WriteAllTextAsync(scriptPath, scriptContent, ct);
            }

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
            _process = Process.Start(psi) ?? throw new Exception($"Failed to start Python service: {ScriptName}");
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // 后台读取 stderr，防止管道阻塞
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_process.HasExited)
                    {
                        var line = await _process.StandardError.ReadLineAsync();
                        if (line == null) break;
                        ConsoleServices.Output?.WriteError($"[{ScriptName} stderr] {line}");
                    }
                }
                catch (ObjectDisposedException) { /* 进程已释放 */ }
                catch (Exception ex)
                {
                    ConsoleServices.Output?.WriteError($"Error reading stderr: {ex.Message}");
                }
            }, ct);

            // 等待服务准备就绪（模型加载等）
            await Task.Delay(StartupDelayMs, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 发送 JSON 请求并等待一行 JSON 响应。
    /// </summary>
    protected async Task<string> SendRequestAsync(string jsonRequest, CancellationToken ct = default)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException($"Python service {ScriptName} is not running.");

        await _stdin!.WriteLineAsync(jsonRequest);
        await _stdin.FlushAsync(ct);

        var response = await _stdout!.ReadLineAsync(ct);
        return response ?? throw new Exception($"No response from Python service {ScriptName}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        finally
        {
            _process?.Dispose();
            _stdin?.Dispose();
            _stdout?.Dispose();
            _process = null;
            _stdin = null;
            _stdout = null;
            _disposed = true;
        }
    }
}