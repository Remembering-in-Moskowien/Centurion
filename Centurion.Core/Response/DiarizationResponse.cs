using Centurion.Core.Models;

namespace Centurion.Core.Response;

/// <summary>
/// 分割结果
/// </summary>
public class DiarizationResponse
{
    public required List<Sentence> Sentences { get; init; }
}