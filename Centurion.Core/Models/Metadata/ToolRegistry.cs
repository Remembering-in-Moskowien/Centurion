// Centurion.Core/Models/Metadata/ToolRegistry.cs
namespace Centurion.Core.Models.Metadata;

public class ToolMeta
{
    public required string ToolName { get; set; }          // 标识，如 "whispercpp", "fasterwhisperxxl"
    public required string DownloadUrl { get; set; }       // 下载包URL（zip或tar）
    public required string ArchiveType { get; set; }       // "zip" 或 "tar.gz"
    public required string ExecutableRelativePath { get; set; } // 解压后可执行文件相对路径
    public string? Version { get; set; }
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
                DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v1.7.5/whisper-bin-x64.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "whisper-cli.exe",
                Version = "v1.7.5"
            },
            ["crispasr"] = new()
            {
                ToolName = "crispasr",
                DownloadUrl = "https://github.com/CrispStrobe/CrispASR/releases/download/v0.8.30/crispasr-windows-x86_64-cpu.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "crispasr.exe", // Linux/macOS 下为 "crispasr"
                Version = "v0.8.29"
            }
        };
}
