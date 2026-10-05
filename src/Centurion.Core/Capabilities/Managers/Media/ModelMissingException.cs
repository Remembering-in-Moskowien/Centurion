namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// Model-missing exception: thrown when the model's local file/directory does not exist; the
/// message includes a prompt to install via <c>Centurion models install &lt;model&gt;</c>.
/// </summary>
public sealed class ModelMissingException(
    string modelName,
    string expectedPath,
    IReadOnlyList<string> missingEntries)
    : InvalidOperationException(BuildMessage(modelName, expectedPath, missingEntries))
{
    /// <summary>The missing model name (the model name in the registry).</summary>
    public string ModelName { get; } = modelName;

    /// <summary>The expected local path (file or directory).</summary>
    public string ExpectedPath { get; } = expectedPath;

    /// <summary>The missing file entries (the model file name in single-file mode).</summary>
    public IReadOnlyList<string> MissingEntries { get; } = missingEntries;

    private static string BuildMessage(string modelName, string expectedPath, IReadOnlyList<string> missingEntries)
    {
        var detail = missingEntries.Count switch
        {
            1 => $"expected at '{expectedPath}'",
            _ => $"expected directory '{expectedPath}' (missing {missingEntries.Count} file(s))"
        };
        return $"Model '{modelName}' is missing ({detail}). " +
               $"Install it first: Centurion models install {modelName}";
    }
}
