namespace Centurion.Core.Operators.Download.Response;

/// <summary>
/// Result of an Aria2 download.
/// </summary>
public class DownloaderResponse
{
    /// <summary>Whether the download succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Path where the file was saved after the download completed.</summary>
    public string FilePath { get; set; } = string.Empty;
}