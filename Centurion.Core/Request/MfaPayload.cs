using Centurion.Core.Models;

namespace Centurion.Core.Request;

/// <summary>
/// MFA 对齐算子请求载荷
/// </summary>
public class MfaRequest
{
    /// <summary>音频文件路径（WAV 格式，16kHz 单声道）</summary>
    public required string AudioFilePath { get; init; }

    /// <summary>包含待对齐单词的句子列表（单词顺序必须与音频一致）</summary>
    public required List<Sentence> Sentences { get; init; }
}