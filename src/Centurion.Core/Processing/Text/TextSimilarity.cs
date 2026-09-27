using System.Buffers;

namespace Centurion.Core.Processing.Text;

/// <summary>
/// 基于编辑距离（Levenshtein）的文本相似度计算工具。
/// </summary>
public static class TextSimilarity
{
    /// <summary>
    /// 计算两段文本的相似度，取值范围 0（完全不同）到 1（完全相同）。
    /// </summary>
    /// <param name="left">第一段文本，为 <see langword="null"/> 时按空串处理。</param>
    /// <param name="right">第二段文本，为 <see langword="null"/> 时按空串处理。</param>
    /// <returns>相似度；两串均为空时返回 1。</returns>
    public static double Similarity(string? left, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        if (left.Length == 0 && right.Length == 0)
            return 1;
        if (left.Length == 0 || right.Length == 0)
            return 0;

        var shorter = left.Length <= right.Length ? left : right;
        var longer = ReferenceEquals(shorter, left) ? right : left;
        var previous = ArrayPool<int>.Shared.Rent(shorter.Length + 1);
        var current = ArrayPool<int>.Shared.Rent(shorter.Length + 1);
        try
        {
            for (var column = 0; column <= shorter.Length; column++)
                previous[column] = column;

            for (var row = 1; row <= longer.Length; row++)
            {
                current[0] = row;
                for (var column = 1; column <= shorter.Length; column++)
                {
                    var substitution = previous[column - 1] + (longer[row - 1] == shorter[column - 1] ? 0 : 1);
                    current[column] = Math.Min(
                        Math.Min(previous[column] + 1, current[column - 1] + 1),
                        substitution);
                }

                (previous, current) = (current, previous);
            }

            var distance = previous[shorter.Length];
            return 1d - (double)distance / Math.Max(left.Length, right.Length);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(previous);
            ArrayPool<int>.Shared.Return(current);
        }
    }
}