// Centurion.Core/Models/Metadata/ToolRegistry.cs
namespace Centurion.Models.Metadata;

/// <summary>
/// 工具的按设备变体（如 CUDA / Vulkan / CPU 构建）。
/// 字段为可空覆盖：null 时回退到 <see cref="ToolMeta"/> 的基础值。
/// </summary>
public class ToolVariant
{
    public string? DownloadUrl { get; set; }
    public string? ArchiveType { get; set; }
    public string? ExecutableRelativePath { get; set; }
    public string? Description { get; set; }
}

public class ToolMeta
{
    public required string ToolName { get; set; }          // 标识，如 "whispercpp", "fasterwhisperxxl"
    public required string DownloadUrl { get; set; }       // 默认（回退）下载包URL（zip或tar）
    public required string ArchiveType { get; set; }       // "zip" 或 "tar.gz"
    public required string ExecutableRelativePath { get; set; } // 解压后可执行文件相对路径
    public string? Version { get; set; }
    /// <summary>
    /// 工具运行时所需的模型/权重基础下载地址（如 demucs-rs 的 safetensors 仓库）。
    /// 可为空；为空时使用工具内置默认地址。
    /// </summary>
    public string? ModelBaseUrl { get; set; }
    /// <summary>
    /// 按设备键（cuda / vulkan / directml / cpu / default）的下载变体。
    /// 设备匹配优先于 "default"，"default" 优先于基础字段。
    /// </summary>
    public IReadOnlyDictionary<string, ToolVariant>? Variants { get; set; }
}

/// <summary>
/// 工具元数据注册表。
/// 实例化对象，由 <see cref="MetadataJsonLoader"/> 在程序启动时从外部 JSON 加载；
/// 未提供外部配置时回退到 <see cref="Default"/>（内置默认条目）。
/// </summary>
public sealed class ToolRegistry
{
    /// <summary>
    /// 内置默认注册表（外部 JSON 缺失时的回退值，也是种子文件的内容来源）。
    /// </summary>
    public static ToolRegistry Default { get; } = new(BuildDefaultTools());

    public IReadOnlyDictionary<string, ToolMeta> Tools { get; }

    public ToolRegistry(IReadOnlyDictionary<string, ToolMeta> tools)
    {
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    private static IReadOnlyDictionary<string, ToolMeta> BuildDefaultTools() =>
        new Dictionary<string, ToolMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["whispercpp"] = new()
            {
                ToolName = "whispercpp",
                DownloadUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "whisper-cli.exe",
                Version = "v1.9.2",
                Variants = new Dictionary<string, ToolVariant>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cuda"] = new()
                    {
                        DownloadUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-cublas-12.4.0-bin-x64.zip",
                        ExecutableRelativePath = "whisper-cli.exe",
                        Description = "CUDA 12.4 build (requires NVIDIA GPU)"
                    }
                }
            },
            ["crispasr"] = new()
            {
                ToolName = "crispasr",
                DownloadUrl = "https://github.com/CrispStrobe/CrispASR/releases/download/v0.8.30/crispasr-windows-x86_64-cpu.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "crispasr.exe", // Linux/macOS 下为 "crispasr"
                Version = "v0.8.29"
            },
            ["demucsrs"] = new()
            {
                ToolName = "demucsrs",
                DownloadUrl = "https://github.com/nikhilunni/demucs-rs/releases/download/v0.3.4/demucs-x86_64-pc-windows-msvc.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "demucs.exe", // Linux/macOS 下为 "demucs"
                Version = "v0.3.4",
                ModelBaseUrl = "https://huggingface.co/set-soft/audio_separation/resolve/main/Demucs/"
            }
        };
}
