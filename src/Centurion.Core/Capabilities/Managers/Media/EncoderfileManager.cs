using System.Runtime.InteropServices;
using System.Text;
using Centurion.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// Manages the encoderfile command-line tool: downloads the CLI on demand, builds encoding
/// artifacts from ONNX models, and runs inference.
/// </summary>
public sealed class EncoderfileManager(
    IServiceProvider serviceProvider,
    ILogger<EncoderfileManager> logger,
    ProcessManager processManager)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<EncoderfileManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ProcessManager _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

    /// <summary>Directory holding the encoderfile tool (located at tools/encoderfile under the application base directory).</summary>
    public string ToolDirectory => Path.Combine(AppContext.BaseDirectory, "tools", "encoderfile");
    /// <summary>Full path to the encoderfile executable, choosing encoderfile.exe or encoderfile based on the OS.</summary>
    public string EncoderfileCliPath => Path.Combine(ToolDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "encoderfile.exe" : "encoderfile");

    /// <summary>
    /// Builds the encoderfile encoding model artifact from an ONNX model directory and model type.
    /// </summary>
    /// <param name="modelDir">ONNX model directory.</param>
    /// <param name="outputPath">Output path for the built artifact.</param>
    /// <param name="modelType">Model type identifier.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    public async Task BuildAsync(string modelDir, string outputPath, string modelType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelType);
        if (!Directory.Exists(modelDir)) throw new DirectoryNotFoundException($"ONNX model directory not found: {modelDir}");

        await EnsureCliAsync(ct);
        Directory.CreateDirectory(ToolDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ToolDirectory);
        var configPath = Path.Combine(ToolDirectory, $"build-{Guid.NewGuid():N}.yaml");
        var config = $"name: {_QuoteYaml(Path.GetFileNameWithoutExtension(outputPath))}{Environment.NewLine}" +
                     $"model_path: {_QuoteYaml(Path.GetFullPath(modelDir))}{Environment.NewLine}" +
                     $"model_type: {_QuoteYaml(modelType)}{Environment.NewLine}" +
                     $"output_path: {_QuoteYaml(Path.GetFullPath(outputPath))}{Environment.NewLine}";
        try
        {
            await File.WriteAllTextAsync(configPath, config, Encoding.UTF8, ct);
            _logger.LogInformation("Building Encoderfile model from {ModelDirectory}.", modelDir);
            await _processManager.ExecuteAsync(EncoderfileCliPath, $"build -f {_QuoteArgument(configPath)}", ct);
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    /// <summary>
    /// Runs inference on the input text using the specified encoderfile model and returns the
    /// command-line standard output.
    /// </summary>
    /// <param name="encoderfilePath">Path to the built encoderfile model.</param>
    /// <param name="input">Input text to run inference on.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>Standard output of the inference command.</returns>
    public async Task<string> InferAsync(string encoderfilePath, string input, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderfilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        if (!File.Exists(encoderfilePath)) throw new FileNotFoundException("Encoderfile model not found.", encoderfilePath);
        var output = await _processManager.ExecuteAsync(encoderfilePath, $"infer --input {_QuoteArgument(input)}", ct);
        _logger.LogDebug("Encoderfile inference completed for {Length} input characters.", input.Length);
        return output;
    }

    private async Task EnsureCliAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(ToolDirectory);
        if (File.Exists(EncoderfileCliPath)) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Encoderfile has no supported prebuilt Windows asset. Place encoderfile.exe in {ToolDirectory}.", ToolDirectory);
            throw new PlatformNotSupportedException($"Encoderfile CLI is unavailable on Windows. Place encoderfile.exe in '{ToolDirectory}'.");
        }

        var asset = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "encoderfile-linux-x86_64"
            : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "encoderfile-macos-arm64" : "encoderfile-macos-x86_64";
        var url = $"https://github.com/mozilla-ai/encoderfile/releases/latest/download/{asset}";
        var downloadUrl = Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.CandidateUrls(url).First();
        _logger.LogInformation("Downloading Encoderfile CLI from {Url}.", downloadUrl);
        using var downloader = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
        await downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
        {
            Payload = new AriaDownloadRequest { Url = downloadUrl, FullSavePath = EncoderfileCliPath, SplitThread = 4, ServerConnection = 4, MaxRetry = 5 }
        }, ct);
        File.SetUnixFileMode(EncoderfileCliPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string _QuoteYaml(string value) => $"'{value.Replace("'", "''")}'";
    private static string _QuoteArgument(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
