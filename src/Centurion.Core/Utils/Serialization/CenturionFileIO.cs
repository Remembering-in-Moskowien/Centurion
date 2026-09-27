namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// Centurion 中间文件（*.centurion.json）的路径辅助工具。
/// 中间文件的读写/校验/迁移统一走 <see cref="ICenturionDocumentStore"/>（System.Text.Json 源生成器）；
/// 本类型仅保留文件名判定与默认输出路径推导。
/// </summary>
public static class CenturionFileIO
{
    /// <summary>中间文件扩展名。</summary>
    public const string Extension = ".centurion.json";

    /// <summary>
    /// 计算中间文件的默认输出路径：输入名 + 可选后缀 + 中间文件扩展名。
    /// 后缀用于避免"输入已是中间文件"时默认输出覆盖输入（如 correct/translate/dub 链式处理）。
    /// </summary>
    /// <param name="inputPath">输入文件路径（媒体/字幕/中间文件均可）。</param>
    /// <param name="suffix">可选后缀（如 "corrected"、"translated"、"dub"）；为空时无后缀。</param>
    /// <returns>默认输出中间文件路径。</returns>
    public static string DefaultOutputPath(string inputPath, string? suffix = null)
    {
        var dir = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileName(inputPath);
        var baseName = IsCenturionFile(fileName)
            ? fileName[..^Extension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        var tail = string.IsNullOrWhiteSpace(suffix) ? string.Empty : $".{suffix}";
        var result = $"{baseName}{tail}{Extension}";
        return string.IsNullOrEmpty(dir) ? result : Path.Combine(dir, result);
    }

    /// <summary>判断路径是否为 Centurion 中间文件（按文件名尾缀 .centurion.json）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是中间文件时为 true。</returns>
    public static bool IsCenturionFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}
