using System.Diagnostics;
using System.Text;
using Centurion.Core.Models;
// localization removed; strings hard-coded

namespace Centurion.Core.Services.Base;

public abstract class PythonServiceBase : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ModelManager? _modelManager;

    protected abstract string ScriptName { get; }
    protected virtual int StartupDelayMs => 8000;

    protected PythonServiceBase(ModelManager? modelManager = null)
    {
        _modelManager = modelManager;
    }

    public virtual async Task EnsureModelAvailableAsync()
    {
        if (_modelManager != null)
            await _modelManager.EnsureModelAvailableAsync();
    }

    public async Task StartAsync(string pythonPath, CancellationToken ct = default)
    {
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

            if (!File.Exists(pythonPath))
                throw new FileNotFoundException(string.Format("Python 3.9+ not found. Please install Python and ensure it is in PATH. ({0})", pythonPath));

            var psi = new ProcessStartInfo(pythonPath, $"\"{scriptPath}\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(scriptPath)
            };

            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["HF_HOME"] = Path.Combine(AppContext.BaseDirectory, "models", "huggingface");
            psi.EnvironmentVariables["TORCH_HOME"] = Path.Combine(AppContext.BaseDirectory, "models", "torch_cache");
            psi.EnvironmentVariables["XDG_CACHE_HOME"] = Path.Combine(AppContext.BaseDirectory, "models", "cache");
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["HF_ENDPOINT"] = "https://hf-mirror.com";

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "models", "huggingface"));
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "models", "torch_cache"));
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "models", "cache"));

            _process = Process.Start(psi);
            if (_process == null)
                throw new Exception($"Failed to start Python process: {ScriptName}");

            _process.EnableRaisingEvents = true;
            _process.Exited += (sender, e) =>
            {
                ConsoleServices.Output?.WriteWarning($"Python exited: {ScriptName} ({_process?.ExitCode ?? -1})");
            };

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

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
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    ConsoleServices.Output?.WriteError($"Error reading stderr: {ex.Message}");
                }
            }, ct);

            await Task.Delay(500, ct);
            if (_process.HasExited)
            {
                string? error = null;
                try { error = await _process.StandardError.ReadToEndAsync(); } catch { }
                throw new Exception($"Python process {ScriptName} exited with code {_process.ExitCode}");
            }

            await Task.Delay(StartupDelayMs, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    protected async Task<string> SendRequestAsync(string jsonRequest, CancellationToken ct = default)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException($"Python not running: {ScriptName} (exit {_process?.ExitCode ?? -1})");

        await _stdin!.WriteLineAsync(jsonRequest);
        await _stdin.FlushAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        string? response;
        try
        {
            do
            {
                response = await _stdout!.ReadLineAsync(cts.Token);
                if (response == null)
                    throw new Exception("Python returned no response");
            } while (!response.TrimStart().StartsWith('{'));
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Python request timed out");
        }
        return response;
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