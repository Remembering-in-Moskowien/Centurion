using Centurion.Core.Factories;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Managers;
using Centurion.Models.Workflow;
using Centurion.Core.Operators.Request;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 人声分离算子（Demucs-rs，可选增强）。
/// 将音频分离出人声轨（--stems vocals），写入 State.VocalsPath，
/// 供转录与说话人分割优先消费。仅在 WorkflowConfig.VocalSeparation 开启时执行；
/// 失败为非致命错误，仅记录警告并回退原始音频继续。
/// <para>
/// 模型获取：demucs-rs 的 safetensors 权重 URL 硬编码且不支持镜像环境变量，
/// 因此本算子会在运行 demucs 前将模型预下载到 demucs-rs 的缓存目录
/// （Windows: %LOCALAPPDATA%\demucs-rs\，Linux: ~/.cache/demucs-rs\，macOS: ~/Library/Caches/demucs-rs\），
/// 官方源下载失败时自动回退 hf-mirror.com 镜像，保证国内网络可用。
/// 模型基础地址可通过 metadata.json 中 demucsrs 条目的 modelBaseUrl 覆盖。
/// </para>
/// </summary>
public sealed class VocalSeparationOperator(
    IToolManagerFactory toolFactory,
    ProcessManager processManager,
    Centurion.Core.Operators.Downloader downloader,
    ILogger<VocalSeparationOperator> logger) : PipelineOperatorBase<VocalSeparationOperator>(logger)
{
    /// <summary>demucs-rs 内置的模型下载基础地址（对应 HF_BASE_URL）。</summary>
    public const string DefaultModelBaseUrl = "https://huggingface.co/set-soft/audio_separation/resolve/main/Demucs/";

    private readonly IToolManagerFactory _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
    private readonly ProcessManager _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    private readonly Centurion.Core.Operators.Downloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Vocal Separation";

    /// <summary>
    /// 执行人声分离：在开关开启且无既有产物时，用 demucs-rs 将输入音频分离出人声轨，
    /// 并将结果写入 <see cref="SubtitleWorkflowContext"/> 状态；分离失败为非致命错误，仅记录警告并回退原始音频。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供配置、状态与输入音频路径。</param>
    /// <param name="cancellationToken">用于取消人声分离过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. 开关（默认关闭：对纯语音素材无价值且耗时长）
        if (!config.VocalSeparation)
        {
            LogInfo("Vocal separation disabled (VocalSeparation = false).");
            return;
        }

        // 2. 检查点：已完成且产物存在则跳过
        if (context.State.IsVocalsSeparated
            && !string.IsNullOrEmpty(context.State.VocalsPath)
            && File.Exists(context.State.VocalsPath))
        {
            LogInfo("Vocal separation already exists, skipping.");
            return;
        }

        // 3. 输入音频（优先预处理后的音频）
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for vocal separation: {inputPath}");
            return;
        }

        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping vocal separation.");
            return;
        }

        var tool = _toolFactory.Create("demucsrs", config.Device);
        try
        {
            // 4. 确保工具已就绪（首次自动下载）
            OnProgress(5, "Ensuring demucs-rs tool...");
            await tool.EnsureToolAsync(cancellationToken);

            // 5. 确保模型已缓存（官方源失败自动回退镜像；失败则不运行 demucs）
            OnProgress(10, $"Ensuring demucs model '{config.VocalSeparationModel}'...");
            if (!await EnsureDemucsModelAsync(tool, config.VocalSeparationModel, cancellationToken))
            {
                LogWarning("Demucs model unavailable; continuing with original audio.");
                return;
            }

            // 6. 执行分离
            var outputDir = Path.Combine(tempDir, $"vocalsep_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);
            var args = BuildArguments(config.VocalSeparationModel, inputPath, outputDir);

            LogInfo($"Running Demucs vocal separation (model {config.VocalSeparationModel})...");
            OnProgress(20, "Separating vocals (this may take a while)...");
            await _processManager.ExecuteAsync(tool.ExecutablePath, args, cancellationToken);

            // 7. 定位人声轨（递归查找，兼容不同输出目录布局）
            var vocalsFile = FindVocalsFile(outputDir);
            if (vocalsFile is null)
            {
                LogWarning("Demucs finished but no 'vocals' stem was found in output; continuing with original audio.");
                return;
            }

            var finalPath = Path.Combine(tempDir, $"vocals_{Guid.NewGuid():N}.wav");
            File.Move(vocalsFile, finalPath);

            context.State.VocalsPath = finalPath;
            context.State.IsVocalsSeparated = true;
            OnProgress(100, "Vocal separation completed.");
            LogInfo($"Vocals saved to: {finalPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 非致命：分离失败不中断字幕生成
            LogWarning($"Vocal separation failed; continuing with original audio. {ex.Message}");
        }
    }

    /// <summary>
    /// 确保 demucs-rs 的模型权重已缓存到其期望的缓存目录。
    /// 已缓存直接返回；否则依次尝试官方源与 hf-mirror 镜像下载。
    /// </summary>
    private async Task<bool> EnsureDemucsModelAsync(ToolManager tool, string model, CancellationToken cancellationToken)
    {
        var fileName = GetDemucsModelFileName(model);
        if (fileName is null)
        {
            LogWarning($"Unknown demucs model '{model}'; cannot pre-download its weights.");
            return false;
        }

        var targetPath = Path.Combine(GetDemucsCacheDir(), fileName);
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            LogInfo($"Demucs model already cached at {targetPath}");
            return true;
        }

        var baseUrl = string.IsNullOrWhiteSpace(tool.ModelBaseUrl) ? DefaultModelBaseUrl : tool.ModelBaseUrl!;
        var urls = new List<string> { baseUrl.TrimEnd('/') + "/" + fileName };
        var mirrorUrl = BuildMirrorUrl(urls[0]);
        if (!string.Equals(mirrorUrl, urls[0], StringComparison.OrdinalIgnoreCase))
            urls.Add(mirrorUrl);

        foreach (var url in urls)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                LogInfo($"Downloading Demucs model '{fileName}' from {url} ...");
                await _downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
                {
                    Payload = new AriaDownloadRequest
                    {
                        Url = url,
                        FullSavePath = targetPath,
                        SplitThread = 8,
                        ServerConnection = 8,
                        MaxRetry = 3,
                        ProgressRefreshMs = 200
                    }
                }, cancellationToken);

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    LogInfo($"Demucs model ready at {targetPath}");
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarning($"Demucs model download failed ({url}): {ex.Message}");
                TryDeletePartialFile(targetPath);
            }
        }

        LogWarning($"Demucs model '{fileName}' could not be downloaded from any source. Check your network or proxy.");
        return false;
    }

    /// <summary>
    /// 构建 Demucs-rs 命令行参数（internal，便于单元测试）。
    /// 真实 CLI：demucs.exe -m &lt;model&gt; -s vocals -o &lt;outputDir&gt; &lt;input&gt;
    /// 输出布局：&lt;outputDir&gt;/&lt;model&gt;/&lt;input-basename&gt;/vocals.wav（由 FindVocalsFile 递归定位）。
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string model, string inputPath, string outputDir) =>
        ["-m", model, "-s", "vocals", "-o", outputDir, inputPath];

    /// <summary>
    /// 在 Demucs 输出目录中递归查找人声轨文件（兼容不同版本/布局差异）。
    /// </summary>
    internal static string? FindVocalsFile(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return null;

        var candidates = Directory.GetFiles(outputDir, "vocals.wav", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(outputDir, "vocals.flac", SearchOption.AllDirectories))
            .ToList();
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// 模型名称 → safetensors 文件名映射（与 demucs-rs metadata.rs 一致；unknown 返回 null）。
    /// </summary>
    internal static string? GetDemucsModelFileName(string model) => model.Trim().ToLowerInvariant() switch
    {
        "htdemucs" => "htdemucs.safetensors",
        "htdemucs_6s" or "htdemucs-6s" => "htdemucs_6s.safetensors",
        "htdemucs_ft" or "htdemucs-ft" => "htdemucs_ft.safetensors",
        _ => null
    };

    /// <summary>
    /// demucs-rs 的模型缓存目录（对应其 dirs::cache_dir().join("demucs-rs")）。
    /// Windows: %LOCALAPPDATA%\demucs-rs；Linux: ~/.cache/demucs-rs；macOS: ~/Library/Caches/demucs-rs。
    /// </summary>
    internal static string GetDemucsCacheDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "demucs-rs");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "demucs-rs");

        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheBase = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : xdg;
        return Path.Combine(cacheBase, "demucs-rs");
    }

    /// <summary>
    /// 将 HuggingFace 官方地址转换为 hf-mirror.com 镜像地址；非官方地址原样返回。
    /// </summary>
    internal static string BuildMirrorUrl(string url) =>
        url.Replace("https://huggingface.co/", "https://hf-mirror.com/", StringComparison.OrdinalIgnoreCase);

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 忽略清理失败，不影响主流程
        }
    }
}
