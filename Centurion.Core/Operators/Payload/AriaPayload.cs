namespace Centurion.Core.Operators.Payload;

/// <summary>
/// Aria2 专用下载载荷（替代魔数字Command数组）
/// </summary>
public class AriaDownloadPayload
{
    /// <summary>下载地址</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>文件完整保存路径</summary>
    public string FullSavePath { get; set; } = string.Empty;

    /// <summary>文件校验哈希值</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>单文件分片数 -x</summary>
    public int SplitThread { get; set; } = 16;

    /// <summary>单服务器连接数 -s</summary>
    public int ServerConnection { get; set; } = 16;

    /// <summary>最大重试次数，0=无限重试</summary>
    public int MaxRetry { get; set; } = 0;

    /// <summary>进度刷新间隔(ms)</summary>
    public int ProgressRefreshMs { get; set; } = 200;
}