using Centurion.Core.Models;

namespace Centurion.Core.Request;

/// <summary>
/// 分割请求载荷
/// </summary>
public class DiarizationRequest
{
    public required string AudioFilePath { get; init; }
    public required List<Sentence> Sentences { get; init; }
}