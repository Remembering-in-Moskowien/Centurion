// 文件：Centurion.Core/Response/AriaDownloadResult.cs
namespace Centurion.Core.Response;

/// <summary>
/// Aria2 下载结果
/// </summary>
public class AriaDownloadResponse
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
}