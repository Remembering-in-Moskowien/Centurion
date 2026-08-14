using System.Diagnostics;
using System.Text;
using Centurion.Core.Exceptions;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Tools;

public class PythonInteropService : IPythonInterop
{
    private readonly IBinaryLocator _binaryLocator;
    private readonly IStringLocalizer<Localization> _localizer;
    private readonly SemaphoreSlim _venvLock = new(1, 1);
    private string? _virtualEnvPythonPath;
    private readonly string _venvRoot = Path.Combine(AppContext.BaseDirectory, "python_env");

    public bool UseTsinghuaMirror { get; set; } = true;

    public PythonInteropService(IBinaryLocator binaryLocator, IStringLocalizer<Localization> localizer)
    {
        _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public async Task<string> LocatePythonAsync(CancellationToken ct = default)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python.exe", "python" }
            : new[] { "python3", "python" };

        foreach (var name in candidates)
        {
            try
            {
                var found = _binaryLocator.Locate(name, "python");
                if (File.Exists(found))
                    return found;
            }
            catch (BinaryNotFoundException) { /* ignore */ }
        }

        // 回退：手动扫描常见 Windows 安装路径
        if (OperatingSystem.IsWindows())
        {
            for (int major = 3; major >= 3; major--)
            for (int minor = 13; minor >= 9; minor--)
            {
                var paths = new[]
                {
                    $@"C:\Python{major}{minor}\python.exe",
                    $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\Programs\Python\Python{major}{minor}\python.exe",
                    $@"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}\Python{major}{minor}\python.exe",
                    $@"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}\Python{major}{minor}\python.exe"
                };
                foreach (var path in paths)
                    if (File.Exists(path))
                        return path;
            }
        }

        throw new InvalidOperationException(_localizer["PythonNotFound"]);
    }

    public async Task<string> EnsureVirtualEnvironmentAsync(CancellationToken ct = default, params string[] packages)
    {
        if (_virtualEnvPythonPath != null && File.Exists(_virtualEnvPythonPath))
            return _virtualEnvPythonPath;

        await _venvLock.WaitAsync(ct);
        try
        {
            if (_virtualEnvPythonPath != null && File.Exists(_virtualEnvPythonPath))
                return _virtualEnvPythonPath;

            var systemPython = await LocatePythonAsync(ct);
            var venvPythonPath = GetVenvPythonPath();

            if (!File.Exists(venvPythonPath))
            {
                ConsoleServices.Output?.WriteLine(_localizer["CreatingVirtualEnv"]);
                await CreateVirtualEnvironmentAsync(systemPython, ct);
                ConsoleServices.Output?.WriteLine(_localizer["VirtualEnvCreated"]);

                if (packages != null && packages.Length > 0)
                {
                    ConsoleServices.Output?.WriteLine(_localizer["InstallingPackages"]);
                    await InstallPackagesToVenvAsync(venvPythonPath, ct, packages);
                    ConsoleServices.Output?.WriteLine(_localizer["PackagesInstalled"]);
                }
            }
            else if (packages != null && packages.Length > 0)
            {
                await InstallMissingPackagesAsync(venvPythonPath, ct, packages);
            }

            _virtualEnvPythonPath = venvPythonPath;
            return _virtualEnvPythonPath;
        }
        finally
        {
            _venvLock.Release();
        }
    }

    public async Task<string> RunScriptAsync(string pythonPath, string scriptPath, string jsonInput, CancellationToken ct = default)
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
        if (process == null)
            throw new Exception(_localizer["FailedToStartPythonProcess"]);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(jsonInput);
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new Exception(string.Format(_localizer["PythonScriptFailed"], process.ExitCode, stderr));

        return stdout;
    }

    public async Task EnsureDependenciesAsync(string pythonPath, CancellationToken ct = default, params string[] packages)
    {
        // 强制使用虚拟环境
        if (!pythonPath.Contains("python_env"))
        {
            await EnsureVirtualEnvironmentAsync(ct, packages);
            return;
        }

        foreach (var fullPackageName in packages)
        {
            var basePackageName = ExtractBasePackageName(fullPackageName);
            var installed = await CheckPackageInstalledAsync(pythonPath, basePackageName, ct);
            if (!installed)
                await InstallPackageAsync(pythonPath, fullPackageName, ct);
        }
    }

    // ---------- 私有辅助方法 ----------
    private string GetVenvPythonPath()
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(_venvRoot, "Scripts", "python.exe")
            : Path.Combine(_venvRoot, "bin", "python");
    }

    private async Task CreateVirtualEnvironmentAsync(string systemPython, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(systemPython, $"-m venv \"{_venvRoot}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception(_localizer["FailedToStartPythonVenv"]);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception(string.Format(_localizer["VirtualEnvCreationFailed"], error));
        }
    }

    private async Task InstallPackagesToVenvAsync(string venvPython, CancellationToken ct, params string[] packages)
    {
        await UpgradePipAsync(venvPython, ct);
        foreach (var fullPackageName in packages)
        {
            var basePackageName = ExtractBasePackageName(fullPackageName);
            if (await CheckPackageInstalledAsync(venvPython, basePackageName, ct))
            {
                ConsoleServices.Output?.WriteLine(string.Format(_localizer["PackageAlreadyInstalled"], basePackageName));
                continue;
            }

            ConsoleServices.Output?.WriteLine(string.Format(_localizer["InstallingPackage"], fullPackageName));
            await InstallPackageAsync(venvPython, fullPackageName, ct);
            ConsoleServices.Output?.WriteLine(string.Format(_localizer["PackageInstalledSuccess"], fullPackageName));
        }
    }

    private async Task InstallMissingPackagesAsync(string venvPython, CancellationToken ct, params string[] packages)
    {
        foreach (var fullPackageName in packages)
        {
            var basePackageName = ExtractBasePackageName(fullPackageName);
            if (!await CheckPackageInstalledAsync(venvPython, basePackageName, ct))
            {
                ConsoleServices.Output?.WriteLine(string.Format(_localizer["InstallingMissingPackage"], fullPackageName));
                await InstallPackageAsync(venvPython, fullPackageName, ct);
                ConsoleServices.Output?.WriteLine(_localizer["PackageInstalledSuccess"]);
            }
        }
    }

    private string ExtractBasePackageName(string fullPackageName)
    {
        var bracketIndex = fullPackageName.IndexOf('[');
        return bracketIndex > 0 ? fullPackageName.Substring(0, bracketIndex) : fullPackageName;
    }

    private async Task UpgradePipAsync(string venvPython, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(venvPython, "-m pip install --upgrade pip")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return;
        await process.WaitForExitAsync(ct);
    }

    private async Task<bool> CheckPackageInstalledAsync(string pythonPath, string package, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(pythonPath, $"-m pip show {package}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception($"无法启动 Python 进程以检查包 '{package}'。");
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    private async Task InstallPackageAsync(string pythonPath, string package, CancellationToken ct)
    {
        var args = $"-m pip install {package}";
        if (UseTsinghuaMirror)
            args += " -i https://pypi.tuna.tsinghua.edu.cn/simple --trusted-host pypi.tuna.tsinghua.edu.cn";
        args += " --extra-index-url https://download.pytorch.org/whl/cpu";

        var psi = new ProcessStartInfo(pythonPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception($"无法启动 Python 进程以安装包 '{package}'。");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new Exception($"安装包 '{package}' 失败 (退出码 {process.ExitCode}): {stderr}");
        }
    }
}