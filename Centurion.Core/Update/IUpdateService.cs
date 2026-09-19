namespace Centurion.Core.Update;

/// <summary>
/// 描述 GitHub Release 中的一个可下载资产（zip 包等）。
/// </summary>
/// <param name="Name">资产文件名，如 Centurion-win-x64.zip</param>
/// <param name="DownloadUrl">下载直链（browser_download_url）</param>
/// <param name="SizeBytes">资产字节大小</param>
public sealed record ReleaseAssetInfo(string Name, string DownloadUrl, long SizeBytes);

/// <summary>
/// GitHub Release 的概要信息（取自 releases/latest 接口）。
/// </summary>
/// <param name="TagName">版本标签，如 v0.2.0</param>
/// <param name="Name">Release 标题</param>
/// <param name="PublishedAt">发布时间（UTC）</param>
/// <param name="Body">Release 说明（Markdown）</param>
/// <param name="Assets">资产列表</param>
public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    DateTimeOffset? PublishedAt,
    string? Body,
    IReadOnlyList<ReleaseAssetInfo> Assets);

/// <summary>
/// 更新检查结果。
/// </summary>
/// <param name="HasUpdate">是否存在可更新的新版本</param>
/// <param name="Latest">最新的 Release 信息（有更新时非空）</param>
/// <param name="Reason">无更新时的人类可读原因（如"仓库尚无 Release"）</param>
public sealed record UpdateCheckResult(bool HasUpdate, GitHubReleaseInfo? Latest, string? Reason);

/// <summary>
/// 更新暂存结果：新版本已下载并解压，更新脚本已生成。
/// </summary>
/// <param name="StagingDirectory">暂存根目录</param>
/// <param name="PayloadDirectory">解压后的新版本文件目录</param>
/// <param name="ScriptPath">更新脚本绝对路径（运行它完成替换）</param>
/// <param name="Asset">被下载的资产</param>
public sealed record UpdateStageResult(
    string StagingDirectory,
    string PayloadDirectory,
    string ScriptPath,
    ReleaseAssetInfo Asset);

/// <summary>
/// 自更新服务：检查 GitHub Releases、对比本地版本、下载并暂存新版本。
/// </summary>
public interface IUpdateService
{
    /// <summary>本地版本号（如 0.1.0）。</summary>
    string LocalVersion { get; }

    /// <summary>
    /// 检查 GitHub 上是否存在比本地更新的版本。
    /// </summary>
    /// <exception cref="HttpRequestException">无法访问 GitHub API 时抛出。</exception>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 下载匹配当前平台的 Release 资产，解压到临时暂存目录，并生成更新脚本。
    /// </summary>
    /// <param name="check"><see cref="CheckAsync"/> 返回的、确认存在更新的结果。</param>
    /// <param name="assetName">手动指定的资产文件名；为空时按平台自动匹配。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    Task<UpdateStageResult> StageAsync(UpdateCheckResult check, string? assetName, CancellationToken cancellationToken);
}
