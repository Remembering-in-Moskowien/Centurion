namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// Path helpers for Centurion intermediate files (*.centurion.json). Reading/writing, validation
/// and migration of intermediate files all go through <see cref="ICenturionDocumentStore"/>
/// (System.Text.Json source generator); this type only keeps filename detection and default
/// output-path derivation.
/// </summary>
public static class CenturionFileIO
{
    /// <summary>The intermediate-file extension.</summary>
    public const string Extension = ".centurion.json";

    /// <summary>
    /// Computes the default output path for the intermediate file: input name + optional suffix +
    /// intermediate-file extension. The suffix avoids overwriting the input when "the input is
    /// already an intermediate file" (e.g. chained correct/translate/dub processing).
    /// </summary>
    /// <param name="inputPath">The input file path (media / subtitle / intermediate file all work).</param>
    /// <param name="suffix">Optional suffix (e.g. "corrected", "translated", "dub"); no suffix when empty.</param>
    /// <returns>The default output intermediate-file path.</returns>
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

    /// <summary>Returns whether a path is a Centurion intermediate file (by the filename suffix .centurion.json).</summary>
    /// <param name="path">The file path.</param>
    /// <returns>true when it is an intermediate file.</returns>
    public static bool IsCenturionFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}
