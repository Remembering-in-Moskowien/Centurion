using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Centurion.Core.Exceptions;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Tools;

/// <summary>
/// Python 交互服务：使用 Miniconda 管理环境，支持依赖缓存和镜像加速。
/// Conda 环境创建在程序目录下的 conda_env/ 中，避免占用用户系统盘。
/// </summary>
public class PythonInteropService(IBinaryLocator binaryLocator, IStringLocalizer<Localization> localizer)
    : IPythonInterop
{
    private readonly IBinaryLocator _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
    private readonly IStringLocalizer<Localization> _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    private readonly SemaphoreSlim _envLock = new(1, 1);
    private string? _condaPythonPath;
    private bool _dependenciesReady;
    private readonly string _condaEnvPrefix = Path.Combine(AppContext.BaseDirectory, "conda_env"); // 环境前缀路径
    private readonly string _condaEnvPythonVersion = "3.11";

    public bool UseTsinghuaMirror { get; set; } = true;

    // 环境前缀路径：程序目录/conda_env/

    // ---------- 公共接口 ----------
    public async Task<string> LocatePythonAsync(CancellationToken ct = default)
    {
        if (_condaPythonPath != null && File.Exists(_condaPythonPath))
            return _condaPythonPath;
        return await EnsureCondaEnvironmentAsync(ct);
    }

    public async Task<string> EnsureVirtualEnvironmentAsync(CancellationToken ct = default, params string[] packages)
    {
        return await EnsureCondaEnvironmentAsync(ct, packages);
    }

    public async Task<string> RunScriptAsync(string pythonPath, string scriptPath, string jsonInput,
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

        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["HF_ENDPOINT"] = "https://hf-mirror.com";

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception(_localizer["FailedToStartPythonProcess"]);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(jsonInput);
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new Exception(string.Format(_localizer["PythonScriptFailed"], process.ExitCode,
                errorBuilder.ToString()));

        return outputBuilder.ToString();
    }

    public async Task EnsureDependenciesAsync(string pythonPath, CancellationToken ct = default,
        params string[] packages)
    {
        if (!pythonPath.Contains("conda_env"))
            await EnsureCondaEnvironmentAsync(ct, packages);
    }

    // ---------- Conda 核心 ----------
    private async Task<string> EnsureCondaEnvironmentAsync(CancellationToken ct = default, params string[] packages)
    {
        if (_condaPythonPath != null && File.Exists(_condaPythonPath) && _dependenciesReady)
            return _condaPythonPath;

        await _envLock.WaitAsync(ct);
        try
        {
            if (_condaPythonPath != null && File.Exists(_condaPythonPath) && _dependenciesReady)
                return _condaPythonPath;

            var condaExe = await LocateCondaAsync(ct);
            if (string.IsNullOrEmpty(condaExe))
                throw new InvalidOperationException("Miniconda not found. Please install Miniconda.");

            await ConfigureCondaChannelsAsync(condaExe, ct);

            // 检查环境前缀路径
            var envPath = await GetCondaEnvPathAsync(condaExe, _condaEnvPrefix, ct);
            if (string.IsNullOrEmpty(envPath) || !Directory.Exists(envPath))
            {
                ConsoleServices.Output.WriteLine($"Creating Conda environment at '{_condaEnvPrefix}'...");
                await RunCondaCommandAsync(condaExe,
                    $"create -p \"{_condaEnvPrefix}\" python={_condaEnvPythonVersion} -y", ct);
                envPath = _condaEnvPrefix;
                if (!Directory.Exists(envPath))
                    throw new Exception($"Failed to create Conda environment at '{_condaEnvPrefix}'.");
            }

            var pythonPath = OperatingSystem.IsWindows()
                ? Path.Combine(envPath, "python.exe")
                : Path.Combine(envPath, "bin", "python");
            if (!File.Exists(pythonPath))
                throw new Exception($"Python not found in Conda environment: {pythonPath}");

            // 仅在依赖未就绪时执行检查
            if (!_dependenciesReady)
            {
                if (packages != null && packages.Length > 0)
                {
                    ConsoleServices.Output.WriteLine("Checking required packages...");
                    await InstallMissingPackagesOnlyAsync(condaExe, envPath, ct, packages);
                }

                _dependenciesReady = true;
            }

            _condaPythonPath = pythonPath;
            return _condaPythonPath;
        }
        finally
        {
            _envLock.Release();
        }
    }

    private async Task ConfigureCondaChannelsAsync(string condaExe, CancellationToken ct)
    {
        var configOutput = await RunCondaCommandAsync(condaExe, "config --get channels", ct);
        if (configOutput.Contains("mirrors.tuna.tsinghua.edu.cn"))
            return;

        var channels = new[]
        {
            "https://mirrors.tuna.tsinghua.edu.cn/anaconda/pkgs/main/",
            "https://mirrors.tuna.tsinghua.edu.cn/anaconda/pkgs/free/",
            "https://mirrors.tuna.tsinghua.edu.cn/anaconda/cloud/conda-forge/"
        };
        foreach (var channel in channels) await RunCondaCommandAsync(condaExe, $"config --add channels {channel}", ct);
        await RunCondaCommandAsync(condaExe, "config --set show_channel_urls yes", ct);
    }

    public async Task<string> LocateCondaAsync(CancellationToken ct = default)
    {
        var condaExe = await LocateCondaInternalAsync(ct);
        if (string.IsNullOrEmpty(condaExe))
            throw new InvalidOperationException("Conda not found.");
        return condaExe;
    }

    public string GetCondaEnvironmentPath()
    {
        return _condaEnvPrefix;
    }

    private async Task<string?> LocateCondaInternalAsync(CancellationToken ct)
    {
        var candidates = OperatingSystem.IsWindows() ? new[] { "conda.exe", "conda" } : new[] { "conda" };
        foreach (var name in candidates)
            try
            {
                var found = _binaryLocator.Locate(name);
                if (File.Exists(found)) return found;
            }
            catch (BinaryNotFoundException)
            {
            }

        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new[]
            {
                Path.Combine(userProfile, "miniconda3", "Scripts", "conda.exe"),
                Path.Combine(userProfile, "anaconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "ProgramData", "miniconda3", "Scripts", "conda.exe")
            };
            foreach (var path in paths)
                if (File.Exists(path))
                    return path;
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            var paths = new[]
            {
                "/opt/miniconda3/bin/conda",
                "/opt/anaconda3/bin/conda",
                $"{home}/miniconda3/bin/conda",
                $"{home}/anaconda3/bin/conda"
            };
            return paths.FirstOrDefault(path => File.Exists(path));
        }

        return null!;
    }

    private async Task<string> RunCondaCommandAsync(string condaExe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(condaExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["LANG"] = "en_US.UTF-8";
        psi.EnvironmentVariables["LC_ALL"] = "en_US.UTF-8";

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Failed to start conda process.");

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                ConsoleServices.Output?.WriteError(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(30));
        await process.WaitForExitAsync(timeoutCts.Token);

        if (process.ExitCode != 0)
            throw new Exception($"Conda command failed (exit {process.ExitCode}): {errorBuilder}");

        return outputBuilder.ToString();
    }

    private async Task<string> GetCondaEnvPathAsync(string condaExe, string prefixPath, CancellationToken ct)
    {
        // 检查目录是否存在且包含 python.exe 或 python
        if (!Directory.Exists(prefixPath))
            return null!;

        var pythonPath = OperatingSystem.IsWindows()
            ? Path.Combine(prefixPath, "python.exe")
            : Path.Combine(prefixPath, "bin", "python");
        if (File.Exists(pythonPath))
            return prefixPath;

        return null!;
    }

    private async Task<HashSet<string>> GetCondaPackageListAsync(string condaExe, string envPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(condaExe, $"list -p \"{envPath}\" --json")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(psi);
        if (process == null) return new HashSet<string>();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) return new HashSet<string>();

        using var doc = JsonDocument.Parse(stdout);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
            if (item.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrEmpty(name))
                    packages.Add(name);
            }

        return packages;
    }

    private async Task<HashSet<string>> GetPipPackageListAsync(string condaExe, string envPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(condaExe, $"run -p \"{envPath}\" pip list --format=json")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(psi);
        if (process == null) return new HashSet<string>();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) return new HashSet<string>();

        using var doc = JsonDocument.Parse(stdout);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
            if (item.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrEmpty(name))
                    packages.Add(name);
            }

        return packages;
    }

    private async Task InstallMissingPackagesOnlyAsync(string condaExe, string envPath, CancellationToken ct,
        params string[] packages)
    {
        // 获取两个包列表（一次进程调用，而不是每个包一次）
        var condaPackages = await GetCondaPackageListAsync(condaExe, envPath, ct);
        var pipPackages = await GetPipPackageListAsync(condaExe, envPath, ct);

        // 检查 ffmpeg（属于 Conda 包）
        if (!condaPackages.Contains("ffmpeg"))
        {
            ConsoleServices.Output?.WriteLine("Installing ffmpeg 7 via Conda...");
            await RunCondaCommandAsync(condaExe, $"install -p \"{envPath}\" -c conda-forge ffmpeg=7 -y", ct);
        }
        else
        {
            ConsoleServices.Output?.WriteLine("ffmpeg already installed.");
        }

        // 升级 pip（轻量操作，始终执行）
        await RunCondaCommandAsync(condaExe, $"run -p \"{envPath}\" python -m pip install --upgrade pip", ct);

        // 按优先级安装 Python 包（先装基础依赖，再装应用包）
        var priorityPackages = new[] { "torch", "numpy", "scipy", "certifi" };
        var remainingPackages = packages.Except(priorityPackages).ToList();

        // 安装优先级包
        foreach (var pkg in priorityPackages)
            if (packages.Contains(pkg))
                await InstallPackageIfMissing(condaExe, envPath, pkg, ct, pipPackages);

        // 安装其余包（diarize, wtpsplit 等）
        foreach (var pkg in remainingPackages) await InstallPackageIfMissing(condaExe, envPath, pkg, ct, pipPackages);
    }

    private async Task InstallPackageIfMissing(string condaExe, string envPath, string package, CancellationToken ct,
        HashSet<string> pipPackages)
    {
        if (pipPackages.Contains(package))
        {
            ConsoleServices.Output?.WriteLine($"Python package '{package}' already installed.");
            return;
        }

        ConsoleServices.Output?.WriteLine($"Installing Python package '{package}'...");
        await InstallPackageWithRetry(condaExe, envPath, package, ct);
    }

    private async Task InstallPackageWithRetry(string condaExe, string envPath, string package, CancellationToken ct,
        int maxRetries = 2)
    {
        var attempt = 0;
        while (attempt < maxRetries)
            try
            {
                var args = $"run -p \"{envPath}\" pip install --default-timeout=1000 {package}";
                if (UseTsinghuaMirror)
                    args += " -i https://pypi.tuna.tsinghua.edu.cn/simple --trusted-host pypi.tuna.tsinghua.edu.cn";
                args += " --extra-index-url https://download.pytorch.org/whl/cpu";
                await RunCondaCommandAsync(condaExe, args, ct);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                ConsoleServices.Output?.WriteWarning(
                    $"安装 {package} 失败 (尝试 {attempt + 1}/{maxRetries})，重试中... 错误: {ex.Message}");
                await Task.Delay(5000 * (attempt + 1), ct);
                attempt++;
            }

        // 最后一次尝试
        await RunCondaCommandAsync(condaExe, $"run -p \"{envPath}\" pip install {package}", ct);
    }
}