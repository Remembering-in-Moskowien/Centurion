namespace Centurion.Core.Operators.Base;

/// <summary>通用模型元数据，所有离线AI模型共用</summary>
public sealed record ModelMeta(string FileName, string DownloadUrl, string Sha256Hash);