namespace Centurion.Core.Operators.Response;

/// <summary>
/// Aria2 下载结果
/// </summary>
public class DownloaderResponse
{
    /// <summary>下载是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>下载完成后文件的保存路径。</summary>
    public string FilePath { get; set; } = string.Empty;
}