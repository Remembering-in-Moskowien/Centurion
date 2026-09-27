using Centurion.Models.Schema;

namespace Centurion.Core.Utils.Serialization;

/// <summary>IR 文件校验结果。</summary>
public sealed class DocumentValidationResult
{
    /// <summary>校验是否通过（合法 JSON + 受支持版本 + 必需结构齐全）。</summary>
    public bool IsValid { get; init; }

    /// <summary>文件声明的 schema 版本（旧格式为 legacy 标记 "0"）。</summary>
    public string? Version { get; init; }

    /// <summary>校验发现的问题列表（为空表示无问题）。</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>便捷创建成功结果。</summary>
    public static DocumentValidationResult Ok(string version) =>
        new() { IsValid = true, Version = version, Issues = [] };

    /// <summary>便捷创建失败结果。</summary>
    public static DocumentValidationResult Fail(string? version, params string[] issues) =>
        new() { IsValid = false, Version = version, Issues = issues };
}

/// <summary>
/// Centurion 中间文件（*.centurion.json）的统一读写入口：
/// 所有 IR 的保存/加载/校验/迁移都必须经由本接口，保证格式契约单一化。
/// </summary>
public interface ICenturionDocumentStore
{
    /// <summary>
    /// 读取中间文件并恢复为 <see cref="CenturionDocument"/>。
    /// 兼容旧版 meta/config/state 格式（自动识别为 legacy，不抛错）；其余非法内容抛
    /// <see cref="InvalidDataException"/>（JSON 损坏 / 缺 config/state / 版本不受支持）。
    /// </summary>
    Task<CenturionDocument> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>把文档序列化（System.Text.Json 源生成器）写入指定路径。</summary>
    Task<string> SaveAsync(CenturionDocument document, string path, CancellationToken cancellationToken);

    /// <summary>
    /// 校验中间文件是否合法：JSON 可解析、版本受支持、config/state 结构齐全。
    /// 不抛异常，问题收集在 <see cref="DocumentValidationResult.Issues"/>。
    /// </summary>
    Task<DocumentValidationResult> ValidateAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 把中间文件迁移到指定版本（当前仅支持 legacy → "1.0"）。
    /// 返回迁移后的文档（不落盘）；已是目标版本时原样返回。
    /// 不支持的迁移目标抛 <see cref="NotSupportedException"/>。
    /// </summary>
    Task<CenturionDocument> MigrateAsync(string path, string toVersion, CancellationToken cancellationToken);
}
