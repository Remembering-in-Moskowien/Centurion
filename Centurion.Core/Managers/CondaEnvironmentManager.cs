using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Centurion.Core.Managers;

/// <summary>
/// Conda 环境管理器：负责定位 Conda、创建环境、配置镜像、安装包（区分 Conda/Pip）。
/// 支持自动安装并使用 mamba 加速包安装。
/// </summary>
public class CondaEnvironmentManager(string? envPrefix = null)
{
    private readonly SemaphoreSlim _envLock = new(1, 1);
    private readonly string _condaEnvPrefix = envPrefix ?? Path.Combine(AppContext.BaseDirectory, "conda_env");
    private readonly string _condaEnvPythonVersion = "3.10";
    private string? _condaPythonPath;
    private bool _dependenciesReady;
    private bool _mambaInstalled;
    private string? _mambaExePath;

    /// <summary>
    /// 是否使用清华镜像（默认启用）
    /// </summary>
    public bool UseTsinghuaMirror { get; set; } = true;

    /// <summary>
    /// 是否自动使用 mamba 加速（默认启用）。启用后将自动安装 mamba 到目标环境。
    /// </summary>
    public bool UseMamba { get; set; } = true;

    /// <summary>
    /// 获取当前 Conda 环境根目录
    /// </summary>
    public string EnvironmentPath => _condaEnvPrefix;

    /// <summary>
    /// 定位系统中可用的 Conda 可执行文件
    /// </summary>
    public async Task<string> LocateCondaAsync(CancellationToken ct = default)
    {
        var condaExe = await LocateCondaInternalAsync(ct);
        if (string.IsNullOrEmpty(condaExe))
            throw new InvalidOperationException("Conda not found. Please install Miniconda.");
        return condaExe;
    }

    /// <summary>
    /// 确保 Conda 环境存在，并安装指定的包列表（自动区分 Conda/Pip）
    /// </summary>
    public async Task EnsurePackagesAsync(string[] packages, CancellationToken ct = default)
    {
        await _envLock.WaitAsync(ct);
        try
        {
            if (_dependenciesReady)
                return;

            var condaExe = await LocateCondaAsync(ct);
            await ConfigureCondaChannelsAsync(condaExe, ct);

            var envPath = await GetOrCreateEnvironmentAsync(condaExe, ct);
            var pythonPath = OperatingSystem.IsWindows()
                ? Path.Combine(envPath, "python.exe")
                : Path.Combine(envPath, "bin", "python");
            if (!File.Exists(pythonPath))
                throw new Exception($"Python not found in Conda environment: {pythonPath}");

            // 自动安装 mamba（如果启用且尚未安装）
            if (UseMamba)
                await EnsureMambaAsync(condaExe, envPath, ct);

            // 安装缺失的包
            await InstallMissingPackagesAsync(condaExe, envPath, ct, packages);
            _condaPythonPath = pythonPath;
            _dependenciesReady = true;
        }
        finally
        {
            _envLock.Release();
        }
    }

    // ---------- 私有方法 ----------

    private async Task<string?> LocateCondaInternalAsync(CancellationToken ct)
    {
        // 尝试从 PATH 中查找
        var candidates = OperatingSystem.IsWindows() ? new[] { "conda.exe", "conda" } : new[] { "conda" };
        foreach (var name in candidates)
        {
            try
            {
                var found = LocateBinary(name);
                if (File.Exists(found))
                    return found;
            }
            catch { /* 忽略 */ }
        }

        // 常见安装路径
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
            foreach (var path in paths)
                if (File.Exists(path))
                    return path;
        }

        return null;
    }

    private string LocateBinary(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            var fullPath = Path.Combine(dir, name);
            if (File.Exists(fullPath))
                return fullPath;
        }
        throw new FileNotFoundException($"Binary '{name}' not found in PATH.");
    }

    private async Task ConfigureCondaChannelsAsync(string condaExe, CancellationToken ct)
    {
        var configOutput = await RunCommandAsync(condaExe, "config --get channels", ct);
        if (configOutput.Contains("mirrors.tuna.tsinghua.edu.cn"))
            return;

        var channels = new[]
        {
            "https://mirrors.tuna.tsinghua.edu.cn/anaconda/pkgs/main/",
            "https://mirrors.tuna.tsinghua.edu.cn/anaconda/cloud/conda-forge/"
        };
        foreach (var channel in channels)
            await RunCommandAsync(condaExe, $"config --add channels {channel}", ct);

        await RunCommandAsync(condaExe, "config --set show_channel_urls yes", ct);
    }

    private async Task<string> GetOrCreateEnvironmentAsync(string condaExe, CancellationToken ct)
    {
        if (Directory.Exists(_condaEnvPrefix))
        {
            var pythonPath = OperatingSystem.IsWindows()
                ? Path.Combine(_condaEnvPrefix, "python.exe")
                : Path.Combine(_condaEnvPrefix, "bin", "python");
            if (File.Exists(pythonPath))
                return _condaEnvPrefix;
        }

        ConsoleServices.Output?.WriteLine($"Creating Conda environment at '{_condaEnvPrefix}'...");
        await RunCommandAsync(condaExe,
            $"create -p \"{_condaEnvPrefix}\" python={_condaEnvPythonVersion} -y", ct);
        if (!Directory.Exists(_condaEnvPrefix))
            throw new Exception($"Failed to create Conda environment at '{_condaEnvPrefix}'.");
        return _condaEnvPrefix;
    }

    /// <summary>
    /// 确保 mamba 已安装到目标环境
    /// </summary>
    private async Task EnsureMambaAsync(string condaExe, string envPath, CancellationToken ct)
    {
        // 检查是否已经安装（通过查找可执行文件）
        var mambaExe = OperatingSystem.IsWindows()
            ? Path.Combine(envPath, "Scripts", "mamba.exe")
            : Path.Combine(envPath, "bin", "mamba");

        if (File.Exists(mambaExe))
        {
            _mambaExePath = mambaExe;
            _mambaInstalled = true;
            ConsoleServices.Output?.WriteLine("mamba already installed in the environment.");
            return;
        }

        // 先检查是否在 PATH 中有 mamba（可能系统全局安装）
        try
        {
            var systemMamba = LocateBinary("mamba");
            if (File.Exists(systemMamba))
            {
                // 如果系统有 mamba，也可以使用，但可能版本不同，为了一致性我们仍然安装到环境
                // 这里可以选择使用系统 mamba 或继续安装环境版本，为了统一，我们安装环境版
            }
        }
        catch { /* 忽略 */ }

        ConsoleServices.Output?.WriteLine("mamba not found in environment. Installing mamba via conda...");
        // 使用 conda 安装 mamba 到目标环境
        await RunCommandAsync(condaExe,
            $"install -p \"{envPath}\" -c conda-forge mamba -y", ct);

        // 再次检查是否存在
        if (File.Exists(mambaExe))
        {
            _mambaExePath = mambaExe;
            _mambaInstalled = true;
            ConsoleServices.Output?.WriteLine("mamba installed successfully.");
        }
        else
        {
            // 如果仍不存在，可能是权限问题，降级到不使用 mamba
            ConsoleServices.Output?.WriteLine("Warning: mamba installation failed. Falling back to conda.");
            _mambaInstalled = false;
            _mambaExePath = null;
        }
    }

    private async Task InstallMissingPackagesAsync(string condaExe, string envPath, CancellationToken ct,
        params string[] packages)
    {
        // 获取已安装的 Conda 和 Pip 包
        var condaPackages = await GetCondaPackageListAsync(condaExe, envPath, ct);
        var pipPackages = await GetPipPackageListAsync(condaExe, envPath, ct);
        var installed = new HashSet<string>(condaPackages, StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in pipPackages)
            installed.Add(pkg);

        // 特殊处理 ffmpeg（使用 Conda）
        if (!condaPackages.Contains("ffmpeg"))
        {
            ConsoleServices.Output?.WriteLine("Installing ffmpeg via Conda...");
            await InstallCondaPackageWithRetry(condaExe, envPath, "ffmpeg", ct);
        }

        // 升级 pip
        await RunCommandAsync(condaExe, $"run -p \"{envPath}\" python -m pip install --upgrade pip", ct);

        // 分区：Conda 包（如 torch, montreal-forced-aligner）和 Pip 包
        var condaOnlyPackages = new[] { "torch", "montreal-forced-aligner" };
        foreach (var pkg in packages)
        {
            if (installed.Contains(pkg))
            {
                ConsoleServices.Output?.WriteLine($"Package '{pkg}' already installed.");
                continue;
            }

            if (condaOnlyPackages.Contains(pkg) || pkg == "ffmpeg") // ffmpeg 已在上面处理
            {
                ConsoleServices.Output?.WriteLine($"Installing Conda package '{pkg}'...");
                await InstallCondaPackageWithRetry(condaExe, envPath, pkg, ct);
            }
            else
            {
                ConsoleServices.Output?.WriteLine($"Installing Pip package '{pkg}'...");
                await InstallPipPackageWithRetry(condaExe, envPath, pkg, ct);
            }
        }
    }

    private async Task InstallCondaPackageWithRetry(string condaExe, string envPath, string package,
        CancellationToken ct, int maxRetries = 2)
    {
        // 决定使用哪个包管理器
        var installer = _mambaInstalled && !string.IsNullOrEmpty(_mambaExePath) ? _mambaExePath : condaExe;
        var isMamba = installer != condaExe;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // mamba 与 conda 的参数基本相同
                await RunCommandAsync(installer!, $"install -p \"{envPath}\" -c conda-forge {package} -y", ct);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                ConsoleServices.Output?.WriteLine($"安装 {package} 失败 (尝试 {attempt + 1}/{maxRetries})，重试中... 错误: {ex.Message}");
                await Task.Delay(5000 * (attempt + 1), ct);
            }
        }
        // 最后一次尝试
        await RunCommandAsync(installer!, $"install -p \"{envPath}\" -c conda-forge {package} -y", ct);
    }

    private async Task InstallPipPackageWithRetry(string condaExe, string envPath, string package,
        CancellationToken ct, int maxRetries = 2)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var args = $"run -p \"{envPath}\" pip install --default-timeout=1000 {package}";
                if (UseTsinghuaMirror)
                    args += " -i https://pypi.tuna.tsinghua.edu.cn/simple --trusted-host pypi.tuna.tsinghua.edu.cn";
                args += " --extra-index-url https://download.pytorch.org/whl/cpu";
                await RunCommandAsync(condaExe, args, ct);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                ConsoleServices.Output?.WriteLine($"安装 {package} 失败 (尝试 {attempt + 1}/{maxRetries})，重试中... 错误: {ex.Message}");
                await Task.Delay(5000 * (attempt + 1), ct);
            }
        }
        // 最后一次尝试
        await RunCommandAsync(condaExe, $"run -p \"{envPath}\" pip install {package}", ct);
    }

    private async Task<HashSet<string>> GetCondaPackageListAsync(string condaExe, string envPath, CancellationToken ct)
    {
        var output = await RunCommandAsync(condaExe, $"list -p \"{envPath}\" --json", ct);
        if (string.IsNullOrEmpty(output)) return [];

        using var doc = JsonDocument.Parse(output);
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
        var output = await RunCommandAsync(condaExe, $"run -p \"{envPath}\" pip list --format=json", ct);
        if (string.IsNullOrEmpty(output)) return [];

        using var doc = JsonDocument.Parse(output);
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

    /// <summary>
    /// 运行任意命令行命令，并返回标准输出
    /// </summary>
    private async Task<string> RunCommandAsync(string exePath, string args, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new FileNotFoundException($"Executable not found: {exePath}");

        var psi = new ProcessStartInfo(exePath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        // 设置环境变量（对于 conda/mamba 都适用）
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["LANG"] = "en_US.UTF-8";
        psi.EnvironmentVariables["LC_ALL"] = "en_US.UTF-8";

        using var process = Process.Start(psi);
        if (process == null) throw new Exception($"Failed to start process: {exePath}");

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                ConsoleServices.Output?.WriteLine(e.Data); // 输出到控制台（可改为日志）
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(30));
        await process.WaitForExitAsync(timeoutCts.Token);

        if (process.ExitCode != 0)
            throw new Exception($"Command failed (exit {process.ExitCode}): {errorBuilder}");

        return outputBuilder.ToString();
    }
}