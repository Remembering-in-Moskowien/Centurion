using Centurion.Core.Models;

namespace Centurion.Core.Response;

/// <summary>
/// MFA 对齐算子返回结果
/// </summary>
public class MfaResponse
{
    /// <summary>更新了精确词级时间戳的句子列表</summary>
    public required List<Sentence> Sentences { get; init; }
}