using System.Collections.Generic;

namespace Centurion.Core.Metadata;

public enum ModelDownloadType
{
    SingleFile,
    Directory
}

/// <summary>通用模型元数据，所有离线AI模型共用</summary>
public record ModelMeta
{
    public string? FileName { get; init; }
    public string? DownloadUrl { get; init; }
    public ModelDownloadType DownloadType { get; init; } = ModelDownloadType.SingleFile;
    public List<string>? Files { get; init; }
    public string? Subdirectory { get; init; }

    // 单文件构造函数（不再需要哈希）
    public ModelMeta(string fileName, string downloadUrl)
    {
        FileName = fileName;
        DownloadUrl = downloadUrl;
        DownloadType = ModelDownloadType.SingleFile;
    }

    // 目录构造函数
    public ModelMeta(string downloadUrl, List<string> files, string? subdirectory = null)
    {
        DownloadUrl = downloadUrl;
        Files = files;
        Subdirectory = subdirectory;
        DownloadType = ModelDownloadType.Directory;
    }
}