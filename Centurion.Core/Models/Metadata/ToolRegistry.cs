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

public static class ToolRegistry
{
    public static IReadOnlyDictionary<string, ToolMeta> Tools { get; } =
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