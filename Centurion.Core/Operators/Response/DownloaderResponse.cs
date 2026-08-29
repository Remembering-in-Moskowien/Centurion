// 文件：Centurion.Core/Response/AriaDownloadResult.cs

namespace Centurion.Core.Operators.Response;

/// <summary>
/// Aria2 下载结果
/// </summary>
public class DownloaderResponse
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
}