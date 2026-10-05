namespace Centurion.Core.Operators.Download.Request;

/// <summary>
/// Aria2-specific download payload (replaces the magic-number Command array).
/// </summary>
public class AriaDownloadRequest
{
    /// <summary>Download URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Full path where the file is saved.</summary>
    public string FullSavePath { get; init; } = string.Empty;

    /// <summary>File verification hash value.</summary>
    public string FileHash { get; init; } = string.Empty;

    /// <summary>Number of chunks per single file (-x).</summary>
    public int SplitThread { get; set; } = 16;

    /// <summary>Number of connections per server (-s).</summary>
    public int ServerConnection { get; set; } = 16;

    /// <summary>Maximum number of retries; 0 = retry indefinitely.</summary>
    public int MaxRetry { get; set; } = 0;

    /// <summary>Progress refresh interval (ms).</summary>
    public int ProgressRefreshMs { get; set; } = 200;
}