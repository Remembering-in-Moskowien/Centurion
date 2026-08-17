using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Tools;
// localization removed; strings hard-coded

namespace Centurion.Core.Operators;

/// <summary>
/// MFA 强制对齐算子，基于 MFA 命令行工具。
/// </summary>
public class MfaCliOperator : IDisposable
{
    // localization removed
    private readonly IPythonInterop _pythonInterop;
    private string? _openFstBinPath;
    private string? _mfaBinaryPath;
    private Process? _runningProcess;
    private bool _disposed;
    private bool _modelsReady;

    private const string AcousticModel = "english_mfa";
    private const string Dictionary = "english_us_mfa";

    public MfaCliOperator(IPythonInterop pythonInterop)
    {
        _pythonInterop = pythonInterop;
    }

    public async Task EnsureTargetAvailableAsync()
    {
        var condaExe = await _pythonInterop.LocateCondaAsync();
        if (string.IsNullOrEmpty(condaExe))
            throw new InvalidOperationException("Conda not found. Please install Miniconda.");

        var condaEnvDir = _pythonInterop.GetCondaEnvironmentPath();
        if (string.IsNullOrEmpty(condaEnvDir))
            throw new InvalidOperationException("Could not determine Conda environment directory.");

        var mfaPath = OperatingSystem.IsWindows()
            ? Path.Combine(condaEnvDir, "Scripts", "mfa.exe")
            : Path.Combine(condaEnvDir, "bin", "mfa");

        _openFstBinPath = OperatingSystem.IsWindows()
            ? Path.Combine(condaEnvDir, "Library", "bin")
            : Path.Combine(condaEnvDir, "bin");

        if (!File.Exists(mfaPath))
        {
            ConsoleServices.Output?.WriteLine("Installing montreal-forced-aligner via Conda...");
            var psi = new ProcessStartInfo(condaExe,
                $"install -p \"{condaEnvDir}\" -c conda-forge montreal-forced-aligner -y")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Failed to start conda install.");

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Conda install failed: {error}");
            }

            if (!File.Exists(mfaPath))
                throw new Exception("montreal-forced-aligner installation succeeded but mfa executable not found.");
            ConsoleServices.Output?.WriteLine("montreal-forced-aligner installed.");
        }

        _mfaBinaryPath = mfaPath;

        if (!_modelsReady)
        {
            await EnsureModelsAvailableAsync();
            _modelsReady = true;
        }
    }

    private async Task EnsureModelsAvailableAsync()
    {
        var acousticCheck = await RunMfaCommandAsync("model list acoustic");
            if (!acousticCheck.Contains(AcousticModel))
            {
                ConsoleServices.Output?.WriteLine("Downloading MFA models...");
                await RunMfaCommandAsync($"model download acoustic {AcousticModel}");
            }

        var dictionaryCheck = await RunMfaCommandAsync("model list dictionary");
        if (!dictionaryCheck.Contains(Dictionary))
        {
            ConsoleServices.Output?.WriteLine("Downloading MFA models...");
            await RunMfaCommandAsync($"model download dictionary {Dictionary}");
        }

        ConsoleServices.Output?.WriteLine("MFA models downloaded.");
    }

    private async Task<string> RunMfaCommandAsync(string arguments)
    {
        if (string.IsNullOrEmpty(_mfaBinaryPath))
            throw new InvalidOperationException("MFA binary not found.");

        var psi = new ProcessStartInfo(_mfaBinaryPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.EnvironmentVariables["HF_ENDPOINT"] = "https://hf-mirror.com";

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start MFA process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception($"MFA command failed: {error}");

        return output;
    }

    public async Task<Dictionary<string, List<MfaWord>>> AlignCorpusAsync(
        string inputDir,
        string outputDir,
        CancellationToken ct = default)
    {
        await EnsureTargetAvailableAsync();

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"输入目录不存在: {inputDir}");

        var wavFiles = Directory.GetFiles(inputDir, "*.wav");
        var labFiles = Directory.GetFiles(inputDir, "*.lab");
        if (wavFiles.Length == 0 || labFiles.Length == 0)
            throw new InvalidOperationException("No .wav or .lab files found in input directory.");

        Directory.CreateDirectory(outputDir);

        var tempDir = Path.Combine(Path.GetDirectoryName(outputDir) ?? outputDir, "mfa_temp");
        Directory.CreateDirectory(tempDir);

        // 修正参数拼接（各参数间添加空格）
        var args = $"align \"{inputDir}\" {Dictionary} {AcousticModel} \"{outputDir}\" " +
                   $"--cleanup_text false --overwrite --single_speaker " +
                   $"--beam 100 --retry_beam 400 " +
                   $"--temporary_directory \"{tempDir}\"";

        await RunMfaCommandAsyncWithCancellation(args, inputDir, ct);

        var results = new Dictionary<string, List<MfaWord>>();
        var textGridFiles = Directory.GetFiles(outputDir, "*.TextGrid");
        foreach (var tgFile in textGridFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(tgFile);
            try
            {
                var words = ParseTextGrid(tgFile);
                results[fileName] = words;
            }
            catch (Exception ex)
            {
                ConsoleServices.Output?.WriteWarning($"Failed to parse TextGrid {tgFile}: {ex.Message}");
            }
        }

        return results;
    }

    private async Task RunMfaCommandAsyncWithCancellation(string arguments, string workingDirectory, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_mfaBinaryPath))
            throw new InvalidOperationException("MFA binary not found.");

        var psi = new ProcessStartInfo(_mfaBinaryPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory
        };

        if (!string.IsNullOrEmpty(_openFstBinPath))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = _openFstBinPath + Path.PathSeparator + currentPath;
        }

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start MFA process.");

        _runningProcess = process;

        try
        {
            ct.Register(() =>
            {
                if (!process.HasExited) process.Kill();
            });

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                throw new Exception($"MFA command failed with exit code {process.ExitCode}. Error: {error}");
            }
        }
        finally
        {
            _runningProcess = null;
        }
    }

    private List<MfaWord> ParseTextGrid(string textGridPath)
    {
        if (!File.Exists(textGridPath))
            throw new FileNotFoundException($"TextGrid file not found: {textGridPath}");

        var content = File.ReadAllText(textGridPath);
        var words = new List<MfaWord>();

        var tierMatch = Regex.Match(content,
            @"item\s*\[\s*(\d+)\s*\]\s*:\s*class\s*=\s*""IntervalTier""[^}]+?name\s*=\s*""(words?)""",
            RegexOptions.Singleline);
        if (!tierMatch.Success)
            throw new Exception("Could not find 'word' tier in TextGrid.");

        var tierStart = tierMatch.Index;
        var tierEnd = content.IndexOf("item [", tierStart + tierMatch.Length);
        if (tierEnd == -1)
            tierEnd = content.Length;
        var tierContent = content.Substring(tierStart, tierEnd - tierStart);

        var intervalRegex =
            new Regex(
                @"intervals\s*\[\s*\d+\s*\][^{]*?xmin\s*=\s*([0-9.]+)[^{]*?xmax\s*=\s*([0-9.]+)[^{]*?text\s*=\s*""([^""]*)""",
                RegexOptions.Singleline);
        var matches = intervalRegex.Matches(tierContent);
        foreach (Match match in matches)
        {
            var start = double.Parse(match.Groups[1].Value);
            var end = double.Parse(match.Groups[2].Value);
            var text = match.Groups[3].Value.Trim();
            text = text.TrimStart('\uFEFF', '\u200B');
            if (!string.IsNullOrEmpty(text) && text != "<unk>" && text != "sp")
                words.Add(new MfaWord { Word = text, Start = start, End = end });
        }

        return words;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_runningProcess != null && !_runningProcess.HasExited)
        {
            _runningProcess.Kill();
            _runningProcess.WaitForExit(5000);
        }

        _runningProcess?.Dispose();
        _runningProcess = null;
        _disposed = true;
    }
}